using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;
using IronPython.Runtime.Exceptions;
using IronPython.Compiler;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using pyRevitAssemblyBuilder.UIManager;

namespace PyRevitLoader {
    /// <summary>
    /// Executes ComboBox event handler scripts using IronPython directly.
    /// Supports: __selfinit__, __cmb_on_change__, __cmb_dropdown_close__, __cmb_dropdown_open__
    ///
    /// The __selfinit__(component, ui_item, uiapp) pattern allows scripts to perform
    /// deferred initialization after the UI is ready. If __selfinit__ returns False,
    /// the ComboBox is deactivated.
    /// </summary>
    public class ComboBoxExecutor {
        private readonly UIApplication _revit;
        private readonly Action<string> _logger;
        private ScriptEngine _engine;
        private ScriptExecutor _scriptExecutor;
        private string _pyrevitLibPath;
        private HashSet<string> _baseSearchPaths;
        
        // Keep engines alive for ComboBoxes with event handlers
        private static readonly List<ScriptEngine> _activeEngines = new List<ScriptEngine>();

        public ComboBoxExecutor(UIApplication uiApplication, Action<string> logger = null) {
            _revit = uiApplication;
            _logger = logger;
        }

        public string Message { get; private set; } = null;

        private void Log(string message) {
            _logger?.Invoke(message);
        }

        /// <summary>
        /// Ensures the IronPython engine is initialized. Called once, reused for all ComboBoxes.
        /// </summary>
        private void EnsureEngineInitialized() {
            if (_engine != null)
                return;

            Log("Initializing shared IronPython engine for ComboBoxes");
            _scriptExecutor = new ScriptExecutor(_revit, false);
            _engine = _scriptExecutor.CreateEngine();

            // Cache base search paths
            _baseSearchPaths = new HashSet<string>(_engine.GetSearchPaths());
        }

        /// <summary>
        /// Executes event handler setup for a ComboBox script file.
        /// </summary>
        /// <param name="scriptPath">Path to the Python script file.</param>
        /// <param name="context">The ComboBoxContext to pass to event handlers.</param>
        /// <param name="comboBox">The Revit ComboBox to wire event handlers to.</param>
        /// <param name="additionalSearchPaths">Additional search paths for imports.</param>
        /// <returns>True if setup succeeded.</returns>
        public bool ExecuteEventHandlerSetup(
            string scriptPath,
            ComboBoxContext context,
            ComboBox comboBox,
            IEnumerable<string> additionalSearchPaths = null) {

            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath)) {
                return true;
            }

            // Only process Python scripts
            if (!scriptPath.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            bool handlersWired = false;

            try {
                // Reuse the engine instead of creating new one each time
                EnsureEngineInitialized();

                // Create a fresh scope for this script (but reuse engine)
                var scope = _scriptExecutor.SetupEnvironment(_engine);

                // Setup search paths for this specific component
                SetupSearchPathsForComponent(_engine, context.directory, additionalSearchPaths);

                // Set __file__ variable
                scope.SetVariable("__file__", scriptPath);

                // Execute the script file to define event handlers
                var script = _engine.CreateScriptSourceFromFile(scriptPath, Encoding.UTF8, SourceCodeKind.File);

                // Compile with proper options
                var compilerOptions = (PythonCompilerOptions)_engine.GetCompilerOptions(scope);
                compilerOptions.ModuleName = "__main__";
                compilerOptions.Module |= IronPython.Runtime.ModuleOptions.Initialize;

                var errors = new ErrorReporter();
                var compiled = script.Compile(compilerOptions, errors);
                if (compiled == null) {
                    Message = string.Join("\r\n", "Compilation failed:", string.Join("\r\n", errors.Errors.ToArray()));
                    Log(Message);
                    return false;
                }

                try {
                    script.Execute(scope);
                }
                catch (SystemExitException) {
                    return true;
                }
                catch (Exception ex) {
                    Message = $"Script execution error: {ex.Message}";
                    Log(Message);
                    return false;
                }

                // Check for __selfinit__ pattern (deferred initializer)
                bool hasSelfInit = scope.ContainsVariable("__selfinit__");
                if (hasSelfInit) {
                    Log($"Found __selfinit__ for ComboBox '{context.name}', invoking...");
                    try {
                        // Create a ui_item wrapper that provides get_rvtapi_object() and other methods
                        // This is done in Python to match the expected pyRevit API
                        scope.SetVariable("__combobox_raw__", comboBox);
                        _engine.Execute(@"
class _ComboBoxUIItemWrapper:
    def __init__(self, cmb):
        self._cmb = cmb
        # Storage for event handler references to prevent GC
        self._handlers = {}

    def get_rvtapi_object(self):
        return self._cmb

    @property
    def current(self):
        return self._cmb.Current

    @current.setter
    def current(self, value):
        self._cmb.Current = value

    @property
    def name(self):
        return self._cmb.Name

    @property
    def enabled(self):
        return self._cmb.Enabled

    @enabled.setter
    def enabled(self, value):
        self._cmb.Enabled = value

    @property
    def visible(self):
        return self._cmb.Visible

    @visible.setter
    def visible(self, value):
        self._cmb.Visible = value

    def get_items(self):
        return list(self._cmb.GetItems())

    def get_title(self):
        return getattr(self._cmb, 'ItemText', self._cmb.Name)

    def set_title(self, text):
        self._cmb.ItemText = text

    def add_item(self, member_data):
        return self._cmb.AddItem(member_data)

    def add_items(self, member_data_list):
        for md in member_data_list:
            self._cmb.AddItem(md)

    def add_separator(self):
        self._cmb.AddSeparator()

    def activate(self):
        self._cmb.Enabled = True
        self._cmb.Visible = True

    def deactivate(self):
        self._cmb.Enabled = False
        self._cmb.Visible = False

    def get_contexthelp(self):
        try:
            return self._cmb.GetContextualHelp()
        except:
            return None

    def set_contexthelp(self, url):
        from Autodesk.Revit.UI import ContextualHelp, ContextualHelpType
        help_obj = ContextualHelp(ContextualHelpType.Url, url)
        self._cmb.SetContextualHelp(help_obj)

__ui_item_wrapper__ = _ComboBoxUIItemWrapper(__combobox_raw__)
", scope);
                        var uiItemWrapper = scope.GetVariable("__ui_item_wrapper__");

                        var selfInitFunc = scope.GetVariable("__selfinit__");
                        var selfInitOps = _engine.Operations;

                        // Call __selfinit__(component, ui_item, uiapp)
                        // component = context, ui_item = wrapper, uiapp = UIApplication
                        var result = selfInitOps.Invoke(selfInitFunc, context, uiItemWrapper, _revit);

                        // If __selfinit__ returns False, deactivate the ComboBox
                        if (result is bool && (bool)result == false) {
                            Log($"__selfinit__ returned False for ComboBox '{context.name}', deactivating.");
                            return false;
                        }

                        Log($"__selfinit__ completed successfully for ComboBox '{context.name}'.");
                        handlersWired = true; // Keep engine alive since __selfinit__ set up handlers
                    }
                    catch (Exception ex) {
                        Message = $"Error in __selfinit__: {ex.Message}";
                        Log(Message);
                        return false;
                    }
                }

                // Check for event handlers
                bool hasOnChange = scope.ContainsVariable("__cmb_on_change__");
                bool hasDropdownClose = scope.ContainsVariable("__cmb_dropdown_close__");
                bool hasDropdownOpen = scope.ContainsVariable("__cmb_dropdown_open__");

                // If __selfinit__ was used, don't require __cmb_* handlers
                if (!hasSelfInit && !hasOnChange && !hasDropdownClose && !hasDropdownOpen) {
                    Log($"No event handlers found in script for ComboBox '{context.name}'.");
                    return true;
                }

                Log($"Found event handlers for ComboBox '{context.name}', wiring up...");
                var ops = _engine.Operations;

                // Wire up __cmb_on_change__ handler
                if (hasOnChange) {
                    var handler = scope.GetVariable("__cmb_on_change__");
                    if (handler != null) {
                        comboBox.CurrentChanged += (sender, args) => {
                            try {
                                ops.Invoke(handler, sender, args, context);
                            }
                            catch (Exception ex) {
                                Log($"Error in __cmb_on_change__: {ex.Message}");
                            }
                        };
                        Log("Wired __cmb_on_change__ handler");
                        handlersWired = true;
                    }
                }

                // Wire up __cmb_dropdown_close__ handler
                if (hasDropdownClose) {
                    var handler = scope.GetVariable("__cmb_dropdown_close__");
                    if (handler != null) {
                        comboBox.DropDownClosed += (sender, args) => {
                            try {
                                ops.Invoke(handler, sender, args, context);
                            }
                            catch (Exception ex) {
                                Log($"Error in __cmb_dropdown_close__: {ex.Message}");
                            }
                        };
                        Log("Wired __cmb_dropdown_close__ handler");
                        handlersWired = true;
                    }
                }

                // Wire up __cmb_dropdown_open__ handler
                if (hasDropdownOpen) {
                    var handler = scope.GetVariable("__cmb_dropdown_open__");
                    if (handler != null) {
                        comboBox.DropDownOpened += (sender, args) => {
                            try {
                                ops.Invoke(handler, sender, args, context);
                            }
                            catch (Exception ex) {
                                Log($"Error in __cmb_dropdown_open__: {ex.Message}");
                            }
                        };
                        Log("Wired __cmb_dropdown_open__ handler");
                        handlersWired = true;
                    }
                }

                // Keep engine alive if handlers were wired
                if (handlersWired) {
                    lock (_activeEngines) {
                        _activeEngines.Add(_engine);
                    }
                    Log($"Event handlers set up successfully for ComboBox '{context.name}'.");
                }

                return true;
            }
            catch (Exception ex) {
                Message = $"ComboBox executor error: {ex.Message}";
                Log(Message);
                return false;
            }
        }

        /// <summary>
        /// Sets up search paths for a specific component, reusing cached base paths.
        /// </summary>
        private void SetupSearchPathsForComponent(ScriptEngine engine, string componentDirectory, IEnumerable<string> additionalSearchPaths) {
            // Start with cached base paths
            var paths = new List<string>(_baseSearchPaths);

            // Add component directory
            if (!string.IsNullOrEmpty(componentDirectory) && Directory.Exists(componentDirectory)) {
                if (!paths.Contains(componentDirectory))
                    paths.Add(componentDirectory);

                // Add lib subdirectory if exists
                var libPath = Path.Combine(componentDirectory, "lib");
                if (Directory.Exists(libPath) && !paths.Contains(libPath))
                    paths.Add(libPath);
            }

            // Find and add pyrevitlib (cached)
            if (_pyrevitLibPath == null) {
                _pyrevitLibPath = FindPyRevitLib(componentDirectory) ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(_pyrevitLibPath) && !paths.Contains(_pyrevitLibPath)) {
                paths.Add(_pyrevitLibPath);

                // Add site-packages
                var pyrevitRoot = Path.GetDirectoryName(_pyrevitLibPath);
                if (!string.IsNullOrEmpty(pyrevitRoot)) {
                    var sitePackages = Path.Combine(pyrevitRoot, "site-packages");
                    if (Directory.Exists(sitePackages) && !paths.Contains(sitePackages))
                        paths.Add(sitePackages);
                }
            }

            // Add additional search paths
            if (additionalSearchPaths != null) {
                foreach (var path in additionalSearchPaths) {
                    if (!string.IsNullOrEmpty(path) && !paths.Contains(path))
                        paths.Add(path);
                }
            }

            engine.SetSearchPaths(paths);
        }

        private string FindPyRevitLib(string componentDirectory) {
            // Strategy 1: Navigate up from component directory
            if (!string.IsNullOrEmpty(componentDirectory)) {
                var current = new DirectoryInfo(componentDirectory);
                int depth = 0;
                while (current != null && depth < 20) {
                    var pyrevitLibPath = Path.Combine(current.FullName, "pyrevitlib");
                    if (Directory.Exists(pyrevitLibPath)) {
                        return pyrevitLibPath;
                    }
                    current = current.Parent;
                    depth++;
                }
            }

            // Strategy 2: Find from this assembly location
            try {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyPath)) {
                    var assemblyDir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath));
                    // Navigate up: engines -> netfx -> bin -> pyRevit root
                    var current = assemblyDir?.Parent?.Parent?.Parent?.Parent;
                    if (current != null) {
                        var pyrevitLibPath = Path.Combine(current.FullName, "pyrevitlib");
                        if (Directory.Exists(pyrevitLibPath)) {
                            return pyrevitLibPath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
