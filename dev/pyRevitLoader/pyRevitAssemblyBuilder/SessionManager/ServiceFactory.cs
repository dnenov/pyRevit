#nullable enable
using Autodesk.Revit.UI;
using pyRevitAssemblyBuilder.AssemblyMaker;
using pyRevitAssemblyBuilder.Interfaces;
using pyRevitAssemblyBuilder.UIManager;
using pyRevitAssemblyBuilder.UIManager.Icons;
using pyRevitAssemblyBuilder.UIManager.Tooltips;

namespace pyRevitAssemblyBuilder.SessionManager
{
    /// <summary>
    /// Factory for creating service instances used by the session manager.
    /// Implements dependency injection by returning interfaces instead of concrete types.
    /// </summary>
    public static class ServiceFactory
    {
        /// <summary>
        /// Creates a logger instance from a Python logger object.
        /// </summary>
        /// <param name="pythonLogger">The Python logger instance.</param>
        /// <returns>An ILogger instance.</returns>
        public static ILogger CreateLogger(object? pythonLogger)
        {
            return new LoggingHelper(pythonLogger);
        }

        /// <summary>
        /// Creates an AssemblyBuilderService instance.
        /// </summary>
        /// <param name="revitVersion">The Revit version number.</param>
        /// <param name="buildStrategy">The build strategy to use.</param>
        /// <param name="logger">The logger instance.</param>
        /// <returns>A new IAssemblyBuilderService instance.</returns>
        public static IAssemblyBuilderService CreateAssemblyBuilderService(string revitVersion, AssemblyBuildStrategy buildStrategy, ILogger logger)
        {
            return new AssemblyBuilderService(revitVersion, buildStrategy, logger);
        }

        /// <summary>
        /// Creates an ExtensionManagerService instance.
        /// </summary>
        /// <returns>A new IExtensionManagerService instance.</returns>
        public static IExtensionManagerService CreateExtensionManagerService()
        {
            return new ExtensionManagerService();
        }

        /// <summary>
        /// Creates a HookManager instance.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <returns>A new IHookManager instance.</returns>
        public static IHookManager CreateHookManager(ILogger logger)
        {
            return new HookManager(logger);
        }

        /// <summary>
        /// Creates an IconManager instance.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <returns>A new IIconManager instance.</returns>
        public static IIconManager CreateIconManager(ILogger logger)
        {
            return new IconManager(logger);
        }

        /// <summary>
        /// Creates a TooltipManager instance.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <returns>A new ITooltipManager instance.</returns>
        public static ITooltipManager CreateTooltipManager(ILogger logger)
        {
            return new TooltipManager(logger);
        }

        /// <summary>
        /// Creates a UIManagerService instance.
        /// </summary>
        /// <param name="uiApplication">The Revit UIApplication instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="iconManager">The icon manager instance.</param>
        /// <param name="tooltipManager">The tooltip manager instance.</param>
        /// <returns>A new IUIManagerService instance.</returns>
        public static IUIManagerService CreateUIManagerService(UIApplication uiApplication, ILogger logger, IIconManager iconManager, ITooltipManager tooltipManager)
        {
            return new UIManagerService(uiApplication, logger, iconManager, tooltipManager);
        }

        /// <summary>
        /// Creates a SessionManagerService instance with all required dependencies.
        /// This is the main entry point for creating a fully configured session manager.
        /// </summary>
        /// <param name="revitVersion">The Revit version number (e.g., "2024").</param>
        /// <param name="buildStrategy">The build strategy to use for assembly generation.</param>
        /// <param name="uiApplication">The Revit UIApplication instance.</param>
        /// <param name="pythonLogger">The Python logger instance for integration with pyRevit's logging system.</param>
        /// <returns>A new ISessionManagerService instance.</returns>
        public static ISessionManagerService CreateSessionManagerService(
            string revitVersion,
            AssemblyBuildStrategy buildStrategy,
            UIApplication uiApplication,
            object? pythonLogger)
        {
            // Create logger first - it's used by all other services
            var logger = CreateLogger(pythonLogger);
            
            // Create individual services with their dependencies
            var assemblyBuilder = CreateAssemblyBuilderService(revitVersion, buildStrategy, logger);
            var extensionManager = CreateExtensionManagerService();
            var hookManager = CreateHookManager(logger);
            var iconManager = CreateIconManager(logger);
            var tooltipManager = CreateTooltipManager(logger);
            var uiManager = CreateUIManagerService(uiApplication, logger, iconManager, tooltipManager);

            return new SessionManagerService(
                assemblyBuilder, 
                extensionManager,
                hookManager,
                uiManager,
                logger);
        }
        
        /// <summary>
        /// Creates a SessionManagerService instance with custom service implementations.
        /// Use this overload for testing or when custom implementations are needed.
        /// </summary>
        /// <param name="assemblyBuilder">Custom assembly builder service.</param>
        /// <param name="extensionManager">Custom extension manager service.</param>
        /// <param name="hookManager">Custom hook manager.</param>
        /// <param name="uiManager">Custom UI manager service.</param>
        /// <param name="logger">Custom logger.</param>
        /// <returns>A new ISessionManagerService instance.</returns>
        public static ISessionManagerService CreateSessionManagerService(
            IAssemblyBuilderService assemblyBuilder,
            IExtensionManagerService extensionManager,
            IHookManager hookManager,
            IUIManagerService uiManager,
            ILogger logger)
        {
            return new SessionManagerService(
                assemblyBuilder,
                extensionManager,
                hookManager,
                uiManager,
                logger);
        }
    }
}
