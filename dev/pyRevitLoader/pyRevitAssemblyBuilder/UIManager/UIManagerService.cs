using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using pyRevitAssemblyBuilder.AssemblyMaker;
using pyRevitAssemblyBuilder.Interfaces;
using pyRevitAssemblyBuilder.UIManager.Icons;
using pyRevitAssemblyBuilder.UIManager.Tooltips;
using Autodesk.Windows;
using RibbonPanel = Autodesk.Revit.UI.RibbonPanel;
using RibbonButton = Autodesk.Windows.RibbonButton;
using RibbonItem = Autodesk.Revit.UI.RibbonItem;
using RevitComboBoxMember = Autodesk.Revit.UI.ComboBoxMember;
using static pyRevitExtensionParser.ExtensionParser;
using pyRevitExtensionParser;
using pyRevitAssemblyBuilder.SessionManager;

namespace pyRevitAssemblyBuilder.UIManager
{
    /// <summary>
    /// Service for building Revit UI elements from parsed extensions.
    /// </summary>
    public class UIManagerService : IUIManagerService
    {
        private readonly ILogger _logger;
        private readonly IIconManager _iconManager;
        private readonly ITooltipManager _tooltipManager;
        private readonly UIApplication _uiApp;
        private ParsedExtension _currentExtension;
        private ComboBoxScriptInitializer _comboBoxScriptInitializer;
        private SmartButtonScriptInitializer _smartButtonScriptInitializer;

        /// <summary>
        /// Gets the UIApplication instance used by this service.
        /// </summary>
        public UIApplication UIApplication => _uiApp;

        /// <summary>
        /// Initializes a new instance of the <see cref="UIManagerService"/> class.
        /// </summary>
        /// <param name="uiApp">The Revit UIApplication instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="iconManager">The icon manager instance.</param>
        /// <param name="tooltipManager">The tooltip manager instance.</param>
        public UIManagerService(UIApplication uiApp, ILogger logger, IIconManager iconManager, ITooltipManager tooltipManager)
        {
            _uiApp = uiApp;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _iconManager = iconManager ?? throw new ArgumentNullException(nameof(iconManager));
            _tooltipManager = tooltipManager ?? throw new ArgumentNullException(nameof(tooltipManager));
            _comboBoxScriptInitializer = new ComboBoxScriptInitializer(uiApp, logger);
            _smartButtonScriptInitializer = new SmartButtonScriptInitializer(uiApp, logger);
        }

        /// <summary>
        /// Builds the UI for the specified extension using the provided assembly information.
        /// </summary>
        /// <param name="extension">The parsed extension containing UI component definitions.</param>
        /// <param name="assemblyInfo">Information about the assembly containing command implementations.</param>
        public void BuildUI(ParsedExtension extension, ExtensionAssemblyInfo assemblyInfo)
        {
            if (extension == null)
            {
                _logger.Warning("Cannot build UI: extension is null.");
                return;
            }
            
            if (assemblyInfo == null)
            {
                _logger.Warning($"Cannot build UI for extension '{extension.Name}': assemblyInfo is null.");
                return;
            }
            
            if (extension.Children == null)
            {
                _logger.Debug($"Extension '{extension.Name}' has no children to build UI for.");
                return;
            }

            // Pre-load icon files in parallel to warm OS file cache
            _iconManager.PreloadExtensionIcons(extension);

            _currentExtension = extension;
            foreach (var component in extension.Children)
            {
                if (component != null)
                {
                    RecursivelyBuildUI(component, null, null, extension.Name, assemblyInfo);
                }
            }
            _currentExtension = null;
        }

        private void RecursivelyBuildUI(
            ParsedComponent component,
            ParsedComponent parentComponent,
            RibbonPanel parentPanel,
            string tabName,
            ExtensionAssemblyInfo assemblyInfo)
        {
            if (component == null)
            {
                _logger.Warning("Cannot build UI: component is null.");
                return;
            }
            
            if (assemblyInfo == null)
            {
                _logger.Warning("Cannot build UI: assemblyInfo is null.");
                return;
            }
            
            if (string.IsNullOrEmpty(tabName))
            {
                _logger.Warning($"Cannot build UI for component '{component.DisplayName}': tabName is null or empty.");
                return;
            }
            
            switch (component.Type)
            {
                case CommandComponentType.Tab:
                    CreateTab(component, assemblyInfo);
                    break;

                case CommandComponentType.Panel:
                    CreatePanel(component, tabName, assemblyInfo);
                    break;

                default:
                    if (component.HasSlideout)
                    {
                        // When a component is marked as a slideout, apply the slideout
                        // but don't process it further (i.e., don't add a separator)
                        EnsureSlideOutApplied(parentComponent, parentPanel);
                    }
                    else
                    {
                        // Only handle the component if it's not a slideout marker
                        HandleComponentBuilding(component, parentPanel, tabName, assemblyInfo);
                    }
                    break;
            }
        }

        /// <summary>
        /// Creates a ribbon tab from the specified component and recursively builds its children.
        /// </summary>
        /// <param name="component">The tab component to create.</param>
        /// <param name="assemblyInfo">Information about the assembly containing command implementations.</param>
        private void CreateTab(ParsedComponent component, ExtensionAssemblyInfo assemblyInfo)
        {
            // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
            var tabText = !string.IsNullOrEmpty(component.Title) ? component.Title : component.DisplayName;
            try 
            { 
                _uiApp.CreateRibbonTab(tabText); 
            } 
            catch (Exception ex)
            {
                // Tab may already exist, which is acceptable - log at debug level
                _logger.Debug($"Failed to create ribbon tab '{tabText}'. Tab may already exist. Exception: {ex.Message}");
            }
            
            // Mark the tab as a pyRevit tab so toggle_icon can find it at runtime
            TagTabAsPyRevit(tabText);
            
            foreach (var child in component.Children ?? Enumerable.Empty<ParsedComponent>())
                RecursivelyBuildUI(child, component, null, tabText, assemblyInfo);
        }

        /// <summary>
        /// Tags a ribbon tab with the pyRevit identifier so the pyrevit.script.toggle_icon() function
        /// can find and update buttons at runtime.
        /// </summary>
        private void TagTabAsPyRevit(string tabName)
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon?.Tabs == null)
                    return;

                // Find the tab by Title (display name) since that's how tabs are identified
                var tab = ribbon.Tabs.FirstOrDefault(t => t.Title == tabName || t.Id == tabName);
                if (tab != null)
                {
                    tab.Tag = UIManagerConstants.PyRevitTabIdentifier;
                    _logger.Debug($"Tagged tab '{tabName}' as pyRevit tab for runtime icon toggling");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to tag tab '{tabName}' as pyRevit tab. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a ribbon panel from the specified component and recursively builds its children.
        /// </summary>
        /// <param name="component">The panel component to create.</param>
        /// <param name="tabName">The name of the tab containing this panel.</param>
        /// <param name="assemblyInfo">Information about the assembly containing command implementations.</param>
        private void CreatePanel(ParsedComponent component, string tabName, ExtensionAssemblyInfo assemblyInfo)
        {
            // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
            var panelText = !string.IsNullOrEmpty(component.Title) ? component.Title : component.DisplayName;
            var panel = _uiApp.GetRibbonPanels(tabName)
                .FirstOrDefault(p => p.Name == panelText)
                ?? _uiApp.CreateRibbonPanel(tabName, panelText);

            // Apply background colors if specified
            ApplyPanelBackgroundColors(panel, component, tabName);

            foreach (var child in component.Children ?? Enumerable.Empty<ParsedComponent>())
                RecursivelyBuildUI(child, component, panel, tabName, assemblyInfo);
        }

        /// <summary>
        /// Checks if a ribbon item with the specified name already exists in the panel.
        /// </summary>
        /// <param name="panel">The ribbon panel to check.</param>
        /// <param name="itemName">The unique name/id of the item to check for.</param>
        /// <returns>True if the item already exists, false otherwise.</returns>
        private bool ItemExistsInPanel(RibbonPanel panel, string itemName)
        {
            if (panel == null || string.IsNullOrEmpty(itemName))
                return false;

            try
            {
                var existingItems = panel.GetItems();
                return existingItems.Any(item => item.Name == itemName);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error checking if item '{itemName}' exists in panel. Exception: {ex.Message}");
                return false;
            }
        }

        private void EnsureSlideOutApplied(ParsedComponent parentComponent,RibbonPanel parentPanel)
        {
            if (parentPanel != null && parentComponent.Type == CommandComponentType.Panel)
            {
                try 
                { 
                    parentPanel.AddSlideOut(); 
                } 
                catch (Exception ex)
                {
                    // Slideout may already exist or panel may not support it - log at debug level
                    _logger.Debug($"Failed to add slideout to panel '{parentPanel.Name}'. Exception: {ex.Message}");
                }
            }
        }

        private void HandleComponentBuilding(
            ParsedComponent component,
            RibbonPanel parentPanel,
            string tabName,
            ExtensionAssemblyInfo assemblyInfo)
        {
            switch (component.Type)
            {
                case CommandComponentType.Separator:
                    // Add separator to panel or pulldown
                    if (parentPanel != null)
                    {
                        try 
                        { 
                            parentPanel.AddSeparator(); 
                        } 
                        catch (Exception ex)
                        {
                            // Separator addition may fail in some contexts - log at debug level
                            _logger.Debug($"Failed to add separator to panel '{parentPanel.Name}'. Exception: {ex.Message}");
                        }
                    }
                    break;
                    
                case CommandComponentType.Stack:
                    BuildStack(component, parentPanel, assemblyInfo);
                    break;
                case CommandComponentType.PanelButton:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        var panelBtnData = CreatePushButton(component, assemblyInfo);
                        var panelBtn = parentPanel.AddItem(panelBtnData) as PushButton;
                        if (panelBtn != null)
                        {
                            _iconManager.ApplyIcon(panelBtn, component);
                            _tooltipManager.ApplyTooltip(panelBtn, component);
                            ApplyHighlightToButton(panelBtn, component);
                            ModifyToPanelButton(tabName, parentPanel, panelBtn);
                        }
                    }
                    break;
                case CommandComponentType.PushButton:
                case CommandComponentType.UrlButton:
                case CommandComponentType.InvokeButton:
                case CommandComponentType.ContentButton:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        var pbData = CreatePushButton(component, assemblyInfo);
                        var btn = parentPanel.AddItem(pbData) as PushButton;
                        if (btn != null)
                        {
                            _iconManager.ApplyIcon(btn, component);
                            _tooltipManager.ApplyTooltip(btn, component);
                            ApplyHighlightToButton(btn, component);
                        }
                    }
                    break;
                    
                case CommandComponentType.SmartButton:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        var sbData = CreatePushButton(component, assemblyInfo);
                        var smartBtn = parentPanel.AddItem(sbData) as PushButton;
                        if (smartBtn != null)
                        {
                            // Apply initial icon (may be overridden by __selfinit__)
                            _iconManager.ApplyIcon(smartBtn, component);
                            _tooltipManager.ApplyTooltip(smartBtn, component);
                            ApplyHighlightToButton(smartBtn, component);
                            
                            // Execute __selfinit__ for SmartButton
                            // This allows the script to set initial icon state (on/off) and other customizations
                            var shouldActivate = _smartButtonScriptInitializer.ExecuteSelfInit(component, smartBtn);
                            if (!shouldActivate)
                            {
                                // If __selfinit__ returns False, disable the button
                                smartBtn.Enabled = false;
                                _logger.Debug($"SmartButton '{component.DisplayName}' deactivated by __selfinit__.");
                            }
                        }
                    }
                    break;
                case CommandComponentType.LinkButton:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        var linkData = CreateLinkButton(component);
                        if (linkData != null)
                        {
                            var linkBtn = parentPanel.AddItem(linkData) as PushButton;
                            if (linkBtn != null)
                            {
                                _iconManager.ApplyIcon(linkBtn, component);
                                _tooltipManager.ApplyTooltip(linkBtn, component);
                                ApplyHighlightToButton(linkBtn, component);
                            }
                        }
                    }
                    break;

                case CommandComponentType.PullDown:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        CreatePulldown(component, parentPanel, tabName, assemblyInfo, true);
                    }
                    break;

                case CommandComponentType.SplitButton:
                case CommandComponentType.SplitPushButton:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        // Use Title from bundle.yaml if available, with config script indicator if applicable
                        var splitButtonText = _tooltipManager.GetButtonTextWithConfigIndicator(component);
                        var splitData = new SplitButtonData(component.DisplayName, splitButtonText);
                        var splitBtn = parentPanel.AddItem(splitData) as SplitButton;
                        if (splitBtn != null)
                        {
                            // Apply icon to split button
                            _iconManager.ApplyIcon(splitBtn, component);

                            // Assign tooltip to the split button itself
                            _tooltipManager.ApplyTooltip(splitBtn, component);

                            // Apply highlight to the split button itself
                            ApplyHighlightToButton(splitBtn, component);

                            foreach (var sub in component.Children ?? Enumerable.Empty<ParsedComponent>())
                            {
                                if (sub.Type == CommandComponentType.Separator)
                                {
                                    // Add separator to split button
                                    try 
                                    { 
                                        splitBtn.AddSeparator(); 
                                    } 
                                    catch (Exception ex)
                                    {
                                        _logger.Debug($"Failed to add separator to split button '{splitButtonText}'. Exception: {ex.Message}");
                                    }
                                }
                                else if (sub.Type == CommandComponentType.PushButton ||
                                         sub.Type == CommandComponentType.UrlButton ||
                                         sub.Type == CommandComponentType.InvokeButton ||
                                         sub.Type == CommandComponentType.ContentButton)
                                {
                                    var subBtn = splitBtn.AddPushButton(CreatePushButton(sub, assemblyInfo));
                                    if (subBtn != null)
                                    {
                                        _iconManager.ApplyIcon(subBtn, sub, component);
                                        _tooltipManager.ApplyTooltip(subBtn, sub);
                                        ApplyHighlightToButton(subBtn, sub);
                                    }
                                }
                                else if (sub.Type == CommandComponentType.LinkButton)
                                {
                                    var subLinkData = CreateLinkButton(sub);
                                    if (subLinkData != null)
                                    {
                                        var linkSubBtn = splitBtn.AddPushButton(subLinkData);
                                        if (linkSubBtn != null)
                                        {
                                            _iconManager.ApplyIcon(linkSubBtn, sub, component);
                                            _tooltipManager.ApplyTooltip(linkSubBtn, sub);
                                            ApplyHighlightToButton(linkSubBtn, sub);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    break;

                case CommandComponentType.ComboBox:
                    if (!ItemExistsInPanel(parentPanel, component.DisplayName))
                    {
                        CreateComboBox(component, parentPanel);
                    }
                    break;
            }
        }

        private void ModifyToPanelButton(string tabName, RibbonPanel parentPanel, PushButton panelBtn)
        {
            try
            {
                var adwTab = ComponentManager
                    .Ribbon
                    .Tabs
                    .FirstOrDefault(t => t.Id == tabName);
                if (adwTab == null)
                    return;

                var adwPanel = adwTab
                    .Panels
                    .First(p => p.Source.Title == parentPanel.Name);
                var adwBtn = adwPanel
                    .Source
                    .Items
                    .First(i => i.AutomationName == panelBtn.ItemText);
                adwPanel.Source.Items.Remove(adwBtn);
                adwPanel.Source.DialogLauncher = (RibbonButton)adwBtn;
            }
            catch (Exception ex)
            {
                // Button modification is non-critical, but log for debugging
                _logger.Debug($"Failed to modify button '{panelBtn.ItemText}' to panel button in tab '{tabName}'. Exception: {ex.Message}");
            }
        }

        private void BuildStack(
            ParsedComponent component,
            RibbonPanel parentPanel,
            ExtensionAssemblyInfo assemblyInfo)
        {
            var itemDataList = new List<RibbonItemData>();
            var originalItems = new List<ParsedComponent>();

            foreach (var child in component.Children ?? Enumerable.Empty<ParsedComponent>())
            {
                // Skip if this item already exists in the panel (e.g., during reload)
                if (ItemExistsInPanel(parentPanel, child.UniqueId))
                {
                    _logger.Debug($"Skipping stack item '{child.UniqueId}' - already exists in panel.");
                    return; // If any item exists, the whole stack was already added
                }

                if (child.Type == CommandComponentType.PushButton ||
                    child.Type == CommandComponentType.SmartButton ||
                    child.Type == CommandComponentType.UrlButton ||
                    child.Type == CommandComponentType.InvokeButton ||
                    child.Type == CommandComponentType.ContentButton)
                {
                    itemDataList.Add(CreatePushButton(child, assemblyInfo));
                    originalItems.Add(child);
                }
                else if (child.Type == CommandComponentType.LinkButton)
                {
                    var linkData = CreateLinkButton(child);
                    if (linkData != null)
                    {
                        itemDataList.Add(linkData);
                        originalItems.Add(child);
                    }
                }
                else if (child.Type == CommandComponentType.PullDown)
                {
                    // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
                    var pulldownText = !string.IsNullOrEmpty(child.Title) ? child.Title : child.DisplayName;
                    // Use DisplayName for the button's internal name to match control ID format
                    var pdData = new PulldownButtonData(child.DisplayName, pulldownText);
                    itemDataList.Add(pdData);
                    originalItems.Add(child);
                }
            }

            if (itemDataList.Count >= 2)
            {
                IList<RibbonItem> stackedItems = null;
                if (itemDataList.Count == 2)
                    stackedItems = parentPanel.AddStackedItems(itemDataList[0], itemDataList[1]);
                else
                    stackedItems = parentPanel.AddStackedItems(itemDataList[0], itemDataList[1], itemDataList[2]);

                if (stackedItems != null)
                {
                    for (int i = 0; i < stackedItems.Count; i++)
                    {
                        var ribbonItem = stackedItems[i];
                        var origComponent = originalItems[i];
                        
                        // Apply icons and tooltips to push buttons in stack
                        if (ribbonItem is PushButton pushBtn)
                        {
                            _iconManager.ApplyIcon(pushBtn, origComponent);
                            _tooltipManager.ApplyTooltip(pushBtn, origComponent);
                            ApplyHighlightToButton(pushBtn, origComponent);
                            
                            // Execute __selfinit__ for SmartButtons in stack
                            if (origComponent.Type == CommandComponentType.SmartButton)
                            {
                                var shouldActivate = _smartButtonScriptInitializer.ExecuteSelfInit(origComponent, pushBtn);
                                if (!shouldActivate)
                                {
                                    pushBtn.Enabled = false;
                                    _logger.Debug($"SmartButton '{origComponent.DisplayName}' in stack deactivated by __selfinit__.");
                                }
                            }
                        }
                        
                        if (ribbonItem is PulldownButton pdBtn)
                        {
                            // Apply icon and tooltip to the pulldown button itself in stack
                            _iconManager.ApplyIcon(pdBtn, origComponent);
                            _tooltipManager.ApplyTooltip(pdBtn, origComponent);
                            
                            // Apply highlight to the pulldown button itself in stack
                            ApplyHighlightToButton(pdBtn, origComponent);

                            foreach (var sub in origComponent.Children ?? Enumerable.Empty<ParsedComponent>())
                            {
                                if (sub.Type == CommandComponentType.Separator)
                                {
                                    // Add separator to pulldown in stack
                                    try 
                                    { 
                                        pdBtn.AddSeparator(); 
                                    } 
                                    catch (Exception ex)
                                    {
                                        _logger.Debug($"Failed to add separator to pulldown button in stack. Exception: {ex.Message}");
                                    }
                                }
                                else if (sub.Type == CommandComponentType.PushButton ||
                                         sub.Type == CommandComponentType.UrlButton ||
                                         sub.Type == CommandComponentType.InvokeButton ||
                                         sub.Type == CommandComponentType.ContentButton)
                                {
                                    var subBtn = pdBtn.AddPushButton(CreatePushButton(sub, assemblyInfo));
                                    if (subBtn != null)
                                    {
                                        _iconManager.ApplyIcon(subBtn, sub, origComponent, IconMode.SmallToBoth);
                                        _tooltipManager.ApplyTooltip(subBtn, sub);
                                        ApplyHighlightToButton(subBtn, sub);
                                    }
                                }
                                else if (sub.Type == CommandComponentType.SmartButton)
                                {
                                    // SmartButtons in pulldowns inside stacks
                                    var smartSubBtn = pdBtn.AddPushButton(CreatePushButton(sub, assemblyInfo));
                                    if (smartSubBtn != null)
                                    {
                                        _iconManager.ApplyIcon(smartSubBtn, sub, origComponent, IconMode.SmallToBoth);
                                        _tooltipManager.ApplyTooltip(smartSubBtn, sub);
                                        ApplyHighlightToButton(smartSubBtn, sub);
                                        
                                        // Execute __selfinit__ for SmartButton in stack pulldown
                                        var shouldActivate = _smartButtonScriptInitializer.ExecuteSelfInit(sub, smartSubBtn);
                                        if (!shouldActivate)
                                        {
                                            smartSubBtn.Enabled = false;
                                            _logger.Debug($"SmartButton '{sub.DisplayName}' in stack pulldown deactivated by __selfinit__.");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private PulldownButtonData CreatePulldown(
            ParsedComponent component,
            RibbonPanel parentPanel,
            string tabName,
            ExtensionAssemblyInfo assemblyInfo,
            bool addToPanel)
        {
            // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
            var pulldownText = !string.IsNullOrEmpty(component.Title) ? component.Title : component.DisplayName;
            // Use DisplayName for the button's internal name to match control ID format
            var pdData = new PulldownButtonData(component.DisplayName, pulldownText);
            if (!addToPanel) return pdData;

            var pdBtn = parentPanel.AddItem(pdData) as PulldownButton;
            if (pdBtn == null) return null;

            // Apply icon and tooltip to the pulldown button itself
            _iconManager.ApplyIcon(pdBtn, component);
            _tooltipManager.ApplyTooltip(pdBtn, component);
            
            // Apply highlight to the pulldown button itself
            ApplyHighlightToButton(pdBtn, component);

            foreach (var sub in component.Children ?? Enumerable.Empty<ParsedComponent>())
            {
                if (sub.Type == CommandComponentType.Separator)
                {
                    // Add separator to pulldown
                    try 
                    { 
                        pdBtn.AddSeparator(); 
                    } 
                    catch (Exception ex)
                    {
                        _logger.Debug($"Failed to add separator to pulldown button '{pulldownText}'. Exception: {ex.Message}");
                    }
                }
                else if (sub.Type == CommandComponentType.PushButton ||
                         sub.Type == CommandComponentType.UrlButton ||
                         sub.Type == CommandComponentType.InvokeButton ||
                         sub.Type == CommandComponentType.ContentButton)
                {
                    var subBtn = pdBtn.AddPushButton(CreatePushButton(sub, assemblyInfo));
                    if (subBtn != null)
                    {
                        _iconManager.ApplyIcon(subBtn, sub, component, IconMode.SmallToBoth);
                        _tooltipManager.ApplyTooltip(subBtn, sub);
                        ApplyHighlightToButton(subBtn, sub);
                    }
                }
                else if (sub.Type == CommandComponentType.SmartButton)
                {
                    // SmartButtons in pulldowns are added as push buttons and execute __selfinit__
                    var smartSubBtn = pdBtn.AddPushButton(CreatePushButton(sub, assemblyInfo));
                    if (smartSubBtn != null)
                    {
                        _iconManager.ApplyIcon(smartSubBtn, sub, component, IconMode.SmallToBoth);
                        _tooltipManager.ApplyTooltip(smartSubBtn, sub);
                        ApplyHighlightToButton(smartSubBtn, sub);
                        
                        // Execute __selfinit__ for SmartButton in pulldown
                        var shouldActivate = _smartButtonScriptInitializer.ExecuteSelfInit(sub, smartSubBtn);
                        if (!shouldActivate)
                        {
                            smartSubBtn.Enabled = false;
                            _logger.Debug($"SmartButton '{sub.DisplayName}' in pulldown deactivated by __selfinit__.");
                        }
                    }
                }
                else if (sub.Type == CommandComponentType.LinkButton)
                {
                    var linkData = CreateLinkButton(sub);
                    if (linkData != null)
                    {
                        var linkSubBtn = pdBtn.AddPushButton(linkData);
                        if (linkSubBtn != null)
                        {
                            _iconManager.ApplyIcon(linkSubBtn, sub, component, IconMode.SmallToBoth);
                            _tooltipManager.ApplyTooltip(linkSubBtn, sub);
                            ApplyHighlightToButton(linkSubBtn, sub);
                        }
                    }
                }
            }
            return pdData;
        }

        private PushButtonData CreatePushButton(ParsedComponent component, ExtensionAssemblyInfo assemblyInfo)
        {
            // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
            var buttonText = _tooltipManager.GetButtonTextWithConfigIndicator(component);
            
            // Ensure the class name matches what the CommandTypeGenerator creates
            var className = SanitizeClassName(component.UniqueId);
            
            // Use DisplayName as the button's internal name to match control ID format.
            // Control IDs use the original folder names with spaces preserved (DisplayName),
            // so the button name must also use DisplayName for findRibbonItem() to work.
            // In Python, component.name is used which preserves spaces.
            var pushButtonData = new PushButtonData(
                component.DisplayName,
                buttonText,
                assemblyInfo.Location,
                className);

            // Set availability class if context is defined
            if (!string.IsNullOrEmpty(component.Context))
            {
                var availabilityClassName = className + "_avail";
                pushButtonData.AvailabilityClassName = availabilityClassName;
            }

            return pushButtonData;
        }

        /// <summary>
        /// Creates a PushButtonData for a LinkButton that directly references an external assembly and class
        /// </summary>
        private PushButtonData CreateLinkButton(ParsedComponent component)
        {
            if (string.IsNullOrEmpty(component.TargetAssembly))
            {
                return null;
            }

            if (string.IsNullOrEmpty(component.CommandClass))
            {
                return null;
            }

            try
            {
                // Resolve the assembly path
                var assemblyPath = ResolveAssemblyPath(component);
                if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
                {
                    return null;
                }

                // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
                // Also add config script indicator if applicable
                var buttonText = _tooltipManager.GetButtonTextWithConfigIndicator(component);

                // Get assembly name to construct fully qualified class names
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                
                // Construct fully qualified class name (Namespace.ClassName)
                // If the class name already contains a dot (is fully qualified), use it as-is
                var fullCommandClass = component.CommandClass.Contains(".") 
                    ? component.CommandClass 
                    : $"{assemblyName}.{component.CommandClass}";

                var pushButtonData = new PushButtonData(
                    component.DisplayName,
                    buttonText,
                    assemblyPath,
                    fullCommandClass);

                // Set availability class if specified
                if (!string.IsNullOrEmpty(component.AvailabilityClass))
                {
                    var fullAvailClass = component.AvailabilityClass.Contains(".")
                        ? component.AvailabilityClass
                        : $"{assemblyName}.{component.AvailabilityClass}";
                    pushButtonData.AvailabilityClassName = fullAvailClass;
                }

                return pushButtonData;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to resolve assembly path for LinkButton '{component.DisplayName}'. Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves the full path to an assembly specified in a LinkButton or InvokeButton
        /// </summary>
        private string ResolveAssemblyPath(ParsedComponent component)
        {
            if (string.IsNullOrWhiteSpace(component.TargetAssembly))
                return null;

            var assemblyValue = component.TargetAssembly.Trim();

            // If it's already a rooted path and exists, return it
            if (Path.IsPathRooted(assemblyValue) && File.Exists(assemblyValue))
                return Path.GetFullPath(assemblyValue);

            // Ensure the assembly has a .dll extension
            var baseFileName = assemblyValue;
            if (!assemblyValue.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                baseFileName += ".dll";

            // Check if path includes directories (relative path)
            var includesDirectories = assemblyValue.Contains(Path.DirectorySeparatorChar) || 
                                    assemblyValue.Contains(Path.AltDirectorySeparatorChar);

            // Try relative path from component directory
            if (!Path.IsPathRooted(assemblyValue) && includesDirectories && !string.IsNullOrEmpty(component.Directory))
            {
                var relativePath = Path.GetFullPath(Path.Combine(component.Directory, assemblyValue));
                if (File.Exists(relativePath))
                    return relativePath;
            }

            // Search in component directory and binary paths
            var searchPaths = new System.Collections.Generic.List<string>();
            
            // Only use expensive CollectBinaryPaths for LinkButton and InvokeButton types
            if (_currentExtension != null && 
                (component.Type == CommandComponentType.LinkButton || component.Type == CommandComponentType.InvokeButton))
            {
                searchPaths.AddRange(_currentExtension.CollectBinaryPaths(component));
            }
            else if (!string.IsNullOrEmpty(component.Directory))
            {
                searchPaths.Add(component.Directory);
                
                // Also check bin subdirectory
                var binPath = Path.Combine(component.Directory, "bin");
                if (Directory.Exists(binPath))
                    searchPaths.Add(binPath);
            }

            // Search in all paths
            foreach (var searchPath in searchPaths)
            {
                var candidatePath = Path.Combine(searchPath, baseFileName);
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            // Search for loaded assembly in AppDomain
            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                var assemblyName = Path.GetFileNameWithoutExtension(baseFileName);
                var loadedAssembly = loadedAssemblies.FirstOrDefault(a => 
                    a.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
                
                if (loadedAssembly != null && !string.IsNullOrEmpty(loadedAssembly.Location))
                    return loadedAssembly.Location;
            }
            catch
            {
                // Ignore errors searching loaded assemblies
            }

            return null;
        }

        /// <summary>
        /// Sanitizes a class name to match the CommandTypeGenerator logic
        /// </summary>
        private static string SanitizeClassName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// Creates a ComboBox control in the ribbon panel with its members.
        /// </summary>
        /// <param name="component">The ComboBox component definition.</param>
        /// <param name="parentPanel">The panel to add the ComboBox to.</param>
        private void CreateComboBox(ParsedComponent component, RibbonPanel parentPanel)
        {
            if (parentPanel == null)
            {
                _logger.Warning($"Cannot create ComboBox '{component.DisplayName}': parent panel is null.");
                return;
            }

            try
            {
                // Use Title from bundle.yaml if available, otherwise fall back to DisplayName
                var comboBoxText = !string.IsNullOrEmpty(component.Title) ? component.Title : component.DisplayName;
                
                // Create ComboBoxData - use DisplayName to match control ID format
                var comboBoxData = new ComboBoxData(component.DisplayName);
                
                // Add ComboBox to panel
                var comboBox = parentPanel.AddItem(comboBoxData) as ComboBox;
                if (comboBox == null)
                {
                    _logger.Warning($"Failed to create ComboBox '{comboBoxText}'.");
                    return;
                }

                // Set tooltip if available
                if (!string.IsNullOrEmpty(component.Tooltip))
                {
                    comboBox.ToolTip = component.Tooltip;
                }

                // Set ItemText (initial display text)
                if (!string.IsNullOrEmpty(comboBoxText))
                {
                    try
                    {
                        comboBox.ItemText = comboBoxText;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Could not set ItemText for ComboBox '{comboBoxText}'. Exception: {ex.Message}");
                    }
                }

                // Add members to ComboBox
                if (component.Members != null && component.Members.Count > 0)
                {
                    foreach (var member in component.Members)
                    {
                        if (string.IsNullOrEmpty(member.Id) || string.IsNullOrEmpty(member.Text))
                        {
                            _logger.Debug("Skipping ComboBox member with missing id or text.");
                            continue;
                        }

                        try
                        {
                            // Create member data
                            var memberData = new ComboBoxMemberData(member.Id, member.Text);
                            
                            // Set group name on data before adding (GroupName is read-only on ComboBoxMember)
                            if (!string.IsNullOrEmpty(member.Group))
                            {
                                memberData.GroupName = member.Group;
                            }
                            
                            // Add member to ComboBox
                            var addedMember = comboBox.AddItem(memberData);
                            
                            if (addedMember != null)
                            {
                                // Set member tooltip if available
                                if (!string.IsNullOrEmpty(member.Tooltip))
                                {
                                    addedMember.ToolTip = member.Tooltip;
                                }

                                // Set member icon if available
                                if (!string.IsNullOrEmpty(member.Icon))
                                {
                                    ApplyIconToComboBoxMember(addedMember, member, component);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Error adding member '{member.Text}' to ComboBox '{comboBoxText}'. Exception: {ex.Message}");
                        }
                    }

                    // Set current selection to first item
                    var items = comboBox.GetItems();
                    if (items != null && items.Count > 0)
                    {
                        try
                        {
                            comboBox.Current = items[0];
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Could not set current item for ComboBox '{comboBoxText}'. Exception: {ex.Message}");
                        }
                    }
                }

                // Apply icon to the ComboBox itself
                _iconManager.ApplyIcon(comboBox, component, null, IconMode.SmallOnly);

                // Execute event handler setup script if present
                if (_comboBoxScriptInitializer != null)
                {
                    try
                    {
                        _comboBoxScriptInitializer.ExecuteEventHandlerSetup(component, comboBox);
                    }
                    catch (Exception scriptEx)
                    {
                        _logger.Warning($"ComboBox event handler setup failed for '{comboBoxText}'. Exception: {scriptEx.Message}");
                    }
                }

                _logger.Debug($"Successfully created ComboBox '{comboBoxText}' with {component.Members?.Count ?? 0} members.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create ComboBox '{component.DisplayName}'. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies icon to a ComboBox member.
        /// </summary>
        private void ApplyIconToComboBoxMember(RevitComboBoxMember member, pyRevitExtensionParser.ComboBoxMember memberDef, ParsedComponent parentComponent)
        {
            if (string.IsNullOrEmpty(memberDef.Icon))
                return;

            try
            {
                // Resolve icon path (relative to bundle directory or absolute)
                var iconPath = memberDef.Icon;
                if (!Path.IsPathRooted(iconPath) && !string.IsNullOrEmpty(parentComponent.Directory))
                {
                    iconPath = Path.Combine(parentComponent.Directory, memberDef.Icon);
                }

                if (File.Exists(iconPath))
                {
                    var bitmap = _iconManager.LoadBitmapSource(iconPath, UIManagerConstants.ICON_SMALL);
                    if (bitmap != null)
                    {
                        member.Image = bitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to apply icon to ComboBox member '{memberDef.Text}'. Exception: {ex.Message}");
            }
        }

        #region Highlight Management

        /// <summary>
        /// Applies highlight to a Revit UI button based on the component's Highlight property
        /// </summary>
        private void ApplyHighlightToButton(RibbonItem revitButton, ParsedComponent component)
        {
            if (string.IsNullOrEmpty(component.Highlight))
                return;

            try
            {
                // Get the Autodesk.Windows.RibbonButton from the Revit RibbonItem
                var adwButton = GetAutodeskWindowsButton(revitButton);
                if (adwButton == null)
                    return;

                // Apply highlight based on the component's Highlight value
                // Use reflection to access the Highlight property since it's in Autodesk.Internal namespace
                var highlightValue = component.Highlight.ToLowerInvariant();
                var highlightProperty = adwButton.GetType().GetProperty("Highlight");
                
                if (highlightProperty != null)
                {
                    var highlightModeType = highlightProperty.PropertyType;
                    object highlightModeValue = null;

                    if (highlightValue == "new")
                    {
                        var enumValues = Enum.GetValues(highlightModeType);
                        foreach (var enumValue in enumValues)
                        {
                            if (enumValue.ToString() == "New")
                            {
                                highlightModeValue = enumValue;
                                break;
                            }
                        }
                    }
                    else if (highlightValue == "updated")
                    {
                        var enumValues = Enum.GetValues(highlightModeType);
                        foreach (var enumValue in enumValues)
                        {
                            if (enumValue.ToString() == "Updated")
                            {
                                highlightModeValue = enumValue;
                                break;
                            }
                        }
                    }

                    if (highlightModeValue != null)
                        highlightProperty.SetValue(adwButton, highlightModeValue);
                }
            }
            catch (Exception ex)            {
                _logger.Debug($"Failed to apply highlight to button '{revitButton?.ItemText ?? "unknown"}'. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the Autodesk.Windows.RibbonButton from a Revit UI RibbonItem
        /// </summary>
        private RibbonButton GetAutodeskWindowsButton(RibbonItem revitButton)
        {
            if (revitButton == null)
                return null;

            try
            {
                // Search for the button in the Autodesk.Windows.ComponentManager.Ribbon
                var ribbon = ComponentManager.Ribbon;
                if (ribbon?.Tabs == null)
                    return null;

                foreach (var tab in ribbon.Tabs)
                {
                    if (tab?.Panels == null)
                        continue;

                    foreach (var panel in tab.Panels)
                    {
                        if (panel?.Source?.Items == null)
                            continue;

                        var found = FindButtonInItems(panel.Source.Items, revitButton.ItemText);
                        if (found != null)
                            return found;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get Autodesk.Windows button for '{revitButton?.ItemText ?? "unknown"}'. Exception: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Converts an ARGB color string to a SolidColorBrush
        /// </summary>
        /// <param name="argbColor">Color string in format #AARRGGBB or #RRGGBB</param>
        /// <returns>SolidColorBrush or null if conversion fails</returns>
        private SolidColorBrush ArgbToBrush(string argbColor)
        {
            if (string.IsNullOrEmpty(argbColor))
                return null;

            try
            {
                // Default values
                string a = "FF", r = "FF", g = "FF", b = "FF";

                // Remove # if present
                argbColor = argbColor.TrimStart('#');

                // Parse color components
                if (argbColor.Length >= 6)
                {
                    b = argbColor.Substring(argbColor.Length - 2, 2);
                    g = argbColor.Substring(argbColor.Length - 4, 2);
                    r = argbColor.Substring(argbColor.Length - 6, 2);
                    
                    if (argbColor.Length >= 8)
                    {
                        a = argbColor.Substring(argbColor.Length - 8, 2);
                    }
                }

                byte alpha = Convert.ToByte(a, 16);
                byte red = Convert.ToByte(r, 16);
                byte green = Convert.ToByte(g, 16);
                byte blue = Convert.ToByte(b, 16);

                return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to convert ARGB color string '{argbColor}' to SolidColorBrush. Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the Autodesk.Windows RibbonPanel for a given Revit RibbonPanel
        /// </summary>
        private Autodesk.Windows.RibbonPanel GetAdWindowsPanel(RibbonPanel revitPanel, string tabName)
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon?.Tabs == null)
                    return null;

                var tab = ribbon.Tabs.FirstOrDefault(t => t.Id == tabName);
                if (tab?.Panels == null)
                    return null;

                return tab.Panels.FirstOrDefault(p => p.Source?.Title == revitPanel.Name);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get Autodesk.Windows panel for '{revitPanel?.Name ?? "unknown"}' in tab '{tabName}'. Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies background colors to a panel based on component settings
        /// </summary>
        private void ApplyPanelBackgroundColors(RibbonPanel revitPanel, ParsedComponent component, string tabName)
        {
            if (component == null)
                return;

            // Check if any background colors are specified
            bool hasBackgroundColors = !string.IsNullOrEmpty(component.PanelBackground) ||
                                      !string.IsNullOrEmpty(component.TitleBackground) ||
                                      !string.IsNullOrEmpty(component.SlideoutBackground);

            if (!hasBackgroundColors)
                return;

            try
            {
                var adwPanel = GetAdWindowsPanel(revitPanel, tabName);
                if (adwPanel == null)
                    return;

                // Reset backgrounds first
                adwPanel.CustomPanelBackground = null;
                adwPanel.CustomPanelTitleBarBackground = null;
                adwPanel.CustomSlideOutPanelBackground = null;

                // Apply panel background - if specified, it sets all three areas
                // This matches Python's set_background() behavior
                if (!string.IsNullOrEmpty(component.PanelBackground))
                {
                    var panelBrush = ArgbToBrush(component.PanelBackground);
                    if (panelBrush != null)
                    {
                        adwPanel.CustomPanelBackground = panelBrush;
                        adwPanel.CustomPanelTitleBarBackground = panelBrush;
                        adwPanel.CustomSlideOutPanelBackground = panelBrush;
                    }
                }

                // Override title background if explicitly specified
                if (!string.IsNullOrEmpty(component.TitleBackground))
                {
                    var titleBrush = ArgbToBrush(component.TitleBackground);
                    if (titleBrush != null)
                        adwPanel.CustomPanelTitleBarBackground = titleBrush;
                }

                // Override slideout background if explicitly specified
                if (!string.IsNullOrEmpty(component.SlideoutBackground))
                {
                    var slideoutBrush = ArgbToBrush(component.SlideoutBackground);
                    if (slideoutBrush != null)
                        adwPanel.CustomSlideOutPanelBackground = slideoutBrush;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to apply background colors to panel '{revitPanel?.Name ?? "unknown"}' in tab '{tabName}'. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively searches for a RibbonButton by AutomationName in a collection of ribbon items
        /// </summary>
        private RibbonButton FindButtonInItems(System.Collections.IEnumerable items, string automationName)
        {
            if (items == null)
                return null;

            foreach (var item in items)
            {
                // Check if this item is the button we're looking for
                if (item is RibbonButton button && button.AutomationName == automationName)
                {
                    return button;
                }
                
                // Check in split buttons
                if (item is RibbonSplitButton splitButton && splitButton.Items != null)
                {
                    var found = FindButtonInItems(splitButton.Items, automationName);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        #endregion
    }
}
