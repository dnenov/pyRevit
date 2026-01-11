#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.UI;
using pyRevitAssemblyBuilder.AssemblyMaker;
using pyRevitAssemblyBuilder.SessionManager;
using pyRevitAssemblyBuilder.UIManager.Icons;
using pyRevitExtensionParser;
using static pyRevitExtensionParser.ExtensionParser;

namespace pyRevitAssemblyBuilder.UIManager.Buttons
{
    /// <summary>
    /// Builder for split buttons.
    /// </summary>
    public class SplitButtonBuilder : ButtonBuilderBase
    {
        private readonly LinkButtonBuilder _linkButtonBuilder;
        private readonly SmartButtonScriptInitializer? _smartButtonScriptInitializer;

        /// <inheritdoc/>
        public override CommandComponentType[] SupportedTypes => new[]
        {
            CommandComponentType.SplitButton,
            CommandComponentType.SplitPushButton
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="SplitButtonBuilder"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="buttonPostProcessor">The button post-processor.</param>
        /// <param name="linkButtonBuilder">The link button builder for child link buttons.</param>
        /// <param name="smartButtonScriptInitializer">Optional SmartButton script initializer.</param>
        public SplitButtonBuilder(
            ILogger logger,
            IButtonPostProcessor buttonPostProcessor,
            LinkButtonBuilder linkButtonBuilder,
            SmartButtonScriptInitializer? smartButtonScriptInitializer = null)
            : base(logger, buttonPostProcessor)
        {
            _linkButtonBuilder = linkButtonBuilder ?? throw new ArgumentNullException(nameof(linkButtonBuilder));
            _smartButtonScriptInitializer = smartButtonScriptInitializer;
        }

        /// <inheritdoc/>
        public override void Build(ParsedComponent component, RibbonPanel parentPanel, string tabName, ExtensionAssemblyInfo assemblyInfo)
        {
            if (parentPanel == null)
            {
                Logger.Warning($"Cannot create split button '{component.DisplayName}': parent panel is null.");
                return;
            }

            if (ItemExistsInPanel(parentPanel, component.DisplayName))
            {
                Logger.Debug($"Split button '{component.DisplayName}' already exists in panel.");
                return;
            }

            try
            {
                // Use Title from bundle.yaml if available, with config script indicator if applicable
                var splitButtonText = ButtonPostProcessor.GetButtonText(component);
                var splitData = new SplitButtonData(component.DisplayName, splitButtonText);
                var splitBtn = parentPanel.AddItem(splitData) as SplitButton;

                if (splitBtn != null)
                {
                    // Apply post-processing to split button
                    ButtonPostProcessor.Process(splitBtn, component);

                    // Add children
                    AddChildrenToSplitButton(splitBtn, component, assemblyInfo);

                    Logger.Debug($"Created split button '{splitButtonText}' with {component.Children?.Count ?? 0} children.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create split button '{component.DisplayName}'. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds child buttons to an existing split button.
        /// </summary>
        public void AddChildrenToSplitButton(SplitButton splitBtn, ParsedComponent component, ExtensionAssemblyInfo assemblyInfo)
        {
            if (splitBtn == null)
            {
                Logger.Warning($"Cannot add children to split button '{component.DisplayName}': splitBtn is null.");
                return;
            }

            // Set synchronization mode BEFORE adding children (required by Revit API for proper initialization)
            // SplitButton: IsSynchronizedWithCurrentItem = true (user's last click determines active button)
            // SplitPushButton: IsSynchronizedWithCurrentItem = false (first button always shows)
            try
            {
                bool shouldSync = component.Type == CommandComponentType.SplitButton;
                splitBtn.IsSynchronizedWithCurrentItem = shouldSync;
                Logger.Debug($"Set IsSynchronizedWithCurrentItem={shouldSync} for split button '{component.DisplayName}' before adding children.");
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to set IsSynchronizedWithCurrentItem for split button '{component.DisplayName}'. Exception: {ex.Message}");
            }

            PushButton? firstButton = null;
            int childCount = 0;
            
            foreach (var sub in component.Children ?? Enumerable.Empty<ParsedComponent>())
            {
                if (sub.Type == CommandComponentType.Separator)
                {
                    // Skip adding separators during reload - they persist in the UI
                    if (assemblyInfo?.IsReloading == true)
                    {
                        Logger.Debug($"Skipping separator during reload for split button '{component.DisplayName}'.");
                        continue;
                    }
                    try
                    {
                        splitBtn.AddSeparator();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to add separator to split button. Exception: {ex.Message}");
                    }
                }
                else if (sub.Type == CommandComponentType.PushButton ||
                         sub.Type == CommandComponentType.SmartButton ||
                         sub.Type == CommandComponentType.UrlButton ||
                         sub.Type == CommandComponentType.InvokeButton ||
                         sub.Type == CommandComponentType.ContentButton)
                {
                    try
                    {
                        var pushButtonData = CreatePushButtonData(sub, assemblyInfo!);
                        var subBtn = splitBtn.AddPushButton(pushButtonData);
                        if (subBtn != null)
                        {
                            ButtonPostProcessor.Process(subBtn, sub, component);
                            // Track first button to set as current
                            firstButton ??= subBtn;
                            childCount++;
                            Logger.Debug($"Added child button '{sub.DisplayName}' to split button '{component.DisplayName}' (child #{childCount}).");
                        }
                        else
                        {
                            Logger.Warning($"AddPushButton returned null for child '{sub.DisplayName}' in split button '{component.DisplayName}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to add child button '{sub.DisplayName}' to split button '{component.DisplayName}'. Exception: {ex.Message}");
                    }
                }
                else if (sub.Type == CommandComponentType.LinkButton)
                {
                    try
                    {
                        var subLinkData = _linkButtonBuilder.CreateLinkButtonData(sub);
                        if (subLinkData != null)
                        {
                            var linkSubBtn = splitBtn.AddPushButton(subLinkData);
                            if (linkSubBtn != null)
                            {
                                ButtonPostProcessor.Process(linkSubBtn, sub, component);
                                // Track first button to set as current
                                firstButton ??= linkSubBtn;
                                childCount++;
                                Logger.Debug($"Added link button '{sub.DisplayName}' to split button '{component.DisplayName}' (child #{childCount}).");
                            }
                            else
                            {
                                Logger.Warning($"AddPushButton returned null for link button '{sub.DisplayName}' in split button '{component.DisplayName}'.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to add link button '{sub.DisplayName}' to split button '{component.DisplayName}'. Exception: {ex.Message}");
                    }
                }
            }
            
            Logger.Debug($"Split button '{component.DisplayName}' has {childCount} children added.");
            
            // Set the first child button as the current button to activate the split button
            // Without a current button set, the split button appears inactive/grayed out
            if (firstButton != null)
            {
                try
                {
                    splitBtn.CurrentButton = firstButton;
                    Logger.Debug($"Set current button for split button '{component.DisplayName}'.");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Failed to set current button for split button '{component.DisplayName}'. Exception: {ex.Message}");
                }
            }
        }
    }
}
