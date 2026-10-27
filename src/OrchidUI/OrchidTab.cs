using System;
using System.Collections.Generic;

namespace OrchidUI
{
    // Same Add* methods as Orchid, without the tab parameter.
    public sealed class OrchidTab
    {
        private readonly Orchid _o;

        internal OrchidTab(Orchid owner, string name)
        {
            _o = owner;
            Name = name;
        }

        public string Name { get; private set; }
        public Orchid Owner { get { return _o; } }

        public Field AddTitle(string text) { return _o.AddTitle(text, Name); }
        public Field AddDescription(string text) { return _o.AddDescription(text, Name); }
        public Field AddSeparator() { return _o.AddSeparator(Name); }
        public Field AddInfoBox(string text, InfoKind kind = InfoKind.Info) { return _o.AddInfoBox(text, Name, kind); }
        public Field AddCustom(Func<object> createElement) { return _o.AddCustom(Name, createElement); }

        public Field AddTextbox(string label, string description, string key, string defaultValue = "", string placeholder = null)
        { return _o.AddTextbox(label, description, Name, key, defaultValue, placeholder); }

        public Field AddPasswordBox(string label, string description, string key, string defaultValue = "")
        { return _o.AddPasswordBox(label, description, Name, key, defaultValue); }

        public Field AddTextArea(string label, string description, string key, string defaultValue = "", int lines = 3)
        { return _o.AddTextArea(label, description, Name, key, defaultValue, lines); }

        public Field AddResponseBox(string label, string description, string key, string defaultValue = "")
        { return _o.AddResponseBox(label, description, Name, key, defaultValue); }

        public Field AddCopyBox(string label, string description, Func<string> text)
        { return _o.AddCopyBox(label, description, Name, text); }

        public Field AddCopyBox(string label, string description, string text)
        { return _o.AddCopyBox(label, description, Name, text); }

        public Field AddToggleSwitch(string label, string description, string key, bool defaultValue)
        { return _o.AddToggleSwitch(label, description, Name, key, defaultValue); }

        public Field AddSegmented(string label, string description, string key, string[] options, int defaultIndex = 0)
        { return _o.AddSegmented(label, description, Name, key, options, defaultIndex); }

        public Field AddSlider(string label, string description, string key, int min, int max, int defaultValue)
        { return _o.AddSlider(label, description, Name, key, min, max, defaultValue); }

        public Field AddSliderWithToggleSwitch(string label, string description, string key, int min, int max, int defaultValue, bool enabledByDefault)
        { return _o.AddSliderWithToggleSwitch(label, description, Name, key, min, max, defaultValue, enabledByDefault); }

        public Field AddDecimalStepper(string label, string description, string key, double min, double max, double step, double defaultValue)
        { return _o.AddDecimalStepper(label, description, Name, key, min, max, step, defaultValue); }

        public Field AddIntegerStepper(string label, string description, string key, int min, int max, int defaultValue, int step = 1)
        { return _o.AddIntegerStepper(label, description, Name, key, min, max, defaultValue, step); }

        public Field AddDropdown(string label, string description, string key, string[] options, int defaultIndex = 0)
        { return _o.AddDropdown(label, description, Name, key, options, defaultIndex); }

        public Field AddRefreshableDropdown(string label, string description, string key, Func<IEnumerable<string>> loadOptions, int defaultIndex = 0)
        { return _o.AddRefreshableDropdown(label, description, Name, key, loadOptions, defaultIndex); }

        public Field AddRefreshableDropdown(string label, string description, string key, string[] options, Action onRefresh, int defaultIndex = 0)
        { return _o.AddRefreshableDropdown(label, description, Name, key, options, onRefresh, defaultIndex); }

        public Field AddMultiSelect(string label, string description, string key, string[] options, string[] defaultSelection = null)
        { return _o.AddMultiSelect(label, description, Name, key, options, defaultSelection); }

        public Field AddMultiSelect(string label, string description, string key, Func<IEnumerable<string>> loadOptions, string[] defaultSelection = null)
        { return _o.AddMultiSelect(label, description, Name, key, loadOptions, defaultSelection); }

        public Field AddColorPicker(string label, string description, string key, string defaultHex = "#FFFFFF", bool allowAlpha = false)
        { return _o.AddColorPicker(label, description, Name, key, defaultHex, allowAlpha); }

        public Field AddDurationPicker(string label, string description, string key, int days, int hours, int minutes, int seconds, bool showDays = true)
        { return _o.AddDurationPicker(label, description, Name, key, days, hours, minutes, seconds, showDays); }

        public Field AddFilePicker(string label, string description, string key, string defaultPath = "",
            string filter = null, string initialDirectory = null, Action onAction = null, string actionText = "Run")
        { return _o.AddFilePicker(label, description, Name, key, defaultPath, filter, initialDirectory, onAction, actionText); }

        public Field AddFolderPicker(string label, string description, string key, string defaultPath = "")
        { return _o.AddFolderPicker(label, description, Name, key, defaultPath); }

        public Field AddButton(string label, string description, string buttonText, Action onClick, bool primary = false)
        { return _o.AddButton(label, description, Name, buttonText, onClick, primary); }

        public Field AddTextList(string label, string description, string key, string[] defaults = null, string placeholder = null)
        { return _o.AddTextList(label, description, Name, key, defaults, placeholder); }

        public Field AddTable(string label, string description, string key, Column[] columns, string[][] defaults = null)
        { return _o.AddTable(label, description, Name, key, columns, defaults); }

        public Field AddSelectionTextList(string label, string description, string key, string[] choices, string[] presets = null,
            string choiceHeader = "Type", string textHeader = "Text")
        { return _o.AddSelectionTextList(label, description, Name, key, choices, presets, choiceHeader, textHeader); }

        public Field AddSelectionAmountTextList(string label, string description, string key, string[] choices, string[] presets = null,
            string choiceHeader = "Type", string amountHeader = "Amount", string textHeader = "Text")
        { return _o.AddSelectionAmountTextList(label, description, Name, key, choices, presets, choiceHeader, amountHeader, textHeader); }

        public Field AddImageInput(string label, string description, string key, string defaultValue = "")
        { return _o.AddImageInput(label, description, Name, key, defaultValue); }
    }
}
