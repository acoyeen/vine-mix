using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Real controls wired exclusively to disposable practice values.</summary>
internal static class HelpControlLessons
{
    internal static IHelpLesson Dials() => new DialLesson();
    internal static IHelpLesson Buttons() => new ButtonsLesson();
    internal static IHelpLesson Search() => new SearchLesson();
    internal static IHelpLesson Feedback() => new FeedbackLesson();

    internal abstract class Lesson : IHelpLesson
    {
        protected readonly StackPanel Root = new() { DataContext = new object() };
        protected readonly List<ComboBox> Choices = [];
        protected readonly List<ContextMenu> Menus = [];
        public FrameworkElement View => Root;

        protected ComboBox Choice(string name, params string[] labels)
        {
            var choice = new ComboBox { MinHeight = 28, Width = 260, Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(choice, name);
            foreach (string label in labels) choice.Items.Add(new ComboBoxItem { Content = label });
            Choices.Add(choice);
            return choice;
        }

        protected static Grid SelectorRow(string label, ComboBox choice)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var text = HelpLessonUi.Text(label);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 0, 10, 0);
            choice.Margin = new Thickness(0);
            row.Children.Add(text); Grid.SetColumn(choice, 1); row.Children.Add(choice);
            return row;
        }

        protected static Grid Instruction(string keys, string action)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(154) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var label = HelpLessonUi.Text(keys, bold: true);
            label.Margin = new Thickness(0, 0, 16, 0);
            var description = HelpLessonUi.Text(action);
            description.Margin = new Thickness(0);
            row.Children.Add(label); Grid.SetColumn(description, 1); row.Children.Add(description);
            return row;
        }

        protected static Grid ButtonRow(Button button, string meaning)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            button.Margin = new Thickness(0);
            var description = HelpLessonUi.Text(meaning);
            description.VerticalAlignment = VerticalAlignment.Center;
            description.Margin = new Thickness(0);
            row.Children.Add(button); Grid.SetColumn(description, 1); row.Children.Add(description);
            return row;
        }

        protected static void StateButton(Button button, bool on, string name, string resource = "ConsoleMuteBrush")
        {
            button.SetResourceReference(Control.BackgroundProperty, on ? resource : "ConsoleButtonFaceBrush");
            button.SetResourceReference(Control.ForegroundProperty, on ? "ConsoleInkBrush" : "ConsoleTextBrush");
            button.SetResourceReference(Control.BorderBrushProperty, on ? resource : "ConsoleControlBorderBrush");
            AutomationProperties.SetName(button, name);
            AutomationProperties.SetItemStatus(button, on ? "On" : "Off");
        }

        public abstract void Reset();

        public virtual bool TryCancel()
        {
            var menu = Menus.FirstOrDefault(item => item.IsOpen);
            if (menu is not null) { menu.IsOpen = false; return true; }
            var choice = Choices.FirstOrDefault(item => item.IsDropDownOpen);
            if (choice is not null) { choice.IsDropDownOpen = false; return true; }
            return false;
        }

        public virtual void Suspend()
        {
            foreach (var menu in Menus) menu.IsOpen = false;
            foreach (var choice in Choices) choice.IsDropDownOpen = false;
            if (Mouse.Captured is DependencyObject captured && Root.IsAncestorOf(captured))
                Mouse.Capture(null);
        }

        public void Dispose() => Suspend();
    }

    internal sealed class DialLesson : Lesson
    {
        private readonly Grid _dialArea = new() { Width = 136, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _example = HelpLessonUi.Hint("");
        private readonly TextBlock _reading = HelpLessonUi.Text("");
        internal ComboBox VariantSelector { get; }
        internal IReadOnlyList<RotaryValueControl> Controls { get; }
        internal RotaryValueControl ActiveControl => Controls[Math.Max(0, VariantSelector.SelectedIndex)];

        internal DialLesson()
        {
            VariantSelector = Choice("Practice dial type", "Level (dB)", "Frequency (Hz)", "Samples (whole numbers)");
            Controls = new RotaryValueControl[]
            {
                new("Level", -60, 12, .1, "dB", 0, _ => RefreshReading(),
                    compact: true, primary: true, smallChange: .5, largeChange: 3,
                    description: "Adjusts level from −60 to +12 dB. 0 dB leaves it unchanged."),
                new("Frequency", 20, 20000, 1, "Hz", 1000, _ => RefreshReading(),
                    logarithmic: true, compact: true, primary: true, smallChange: 10, largeChange: 100,
                    description: "Adjusts frequency from 20 to 20,000 Hz on a logarithmic scale."),
                new("Samples", 0, 96000, 1, "", 480, _ => RefreshReading(),
                    compact: true, integer: true, smallChange: 1, largeChange: 48,
                    description: "Accepts whole samples. At 48 kHz, 48 samples equal one millisecond.")
            };
            foreach (var control in Controls)
            {
                control.Visibility = Visibility.Collapsed;
                _dialArea.Children.Add(control);
            }
            VariantSelector.SelectionChanged += (_, _) => ShowVariant();
            var demo = new Grid { Margin = new Thickness(0, 6, 0, 4) };
            demo.ColumnDefinitions.Add(new() { Width = new GridLength(156) });
            demo.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            demo.Children.Add(_dialArea);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(_example); detail.Children.Add(_reading);
            Grid.SetColumn(detail, 1); demo.Children.Add(detail);
            Root.Children.Add(HelpLessonUi.Section("", "", SelectorRow("Control", VariantSelector), demo));
            Root.Children.Add(HelpLessonUi.Section("Adjustments", "",
                Instruction("Drag up / down", "Change the value. Hold Shift for finer movement."),
                Instruction("Click the value", "Type an exact number. Enter applies; Escape cancels the draft."),
                Instruction("Tab / Shift+Tab", "Apply and move to the next / previous control. Correct an invalid value before moving on."),
                Instruction("Arrow keys", "Make small adjustments to a focused dial."),
                Instruction("Page Up / Down", "Make larger adjustments."),
                Instruction("Double-click / Home", "Restore the default value."),
                HelpLessonUi.Hint("Wheel: scroll Help. On the workspace canvas, the wheel zooms without changing values.")));
            Reset();
        }

        private void ShowVariant()
        {
            int selected = Math.Clamp(VariantSelector.SelectedIndex, 0, Controls.Count - 1);
            for (int i = 0; i < Controls.Count; i++)
            {
                if (i != selected) Controls[i].SuspendPracticeInteraction();
                Controls[i].Visibility = i == selected ? Visibility.Visible : Visibility.Collapsed;
            }
            _example.Text = selected switch
            {
                1 => "20–20,000 Hz. Equal movement changes frequency proportionally.",
                2 => "0–96,000 samples. Whole numbers only; 480.5 is rejected.",
                _ => "−60 to +12 dB. Exact entry also accepts values such as −6.25 dB."
            };
            RefreshReading();
        }

        private void RefreshReading()
        {
            if (Controls is null || VariantSelector.SelectedIndex < 0) return;
            double value = ActiveControl.Value;
            _reading.Text = VariantSelector.SelectedIndex switch
            {
                1 => "Reset value: 1,000 Hz",
                2 => $"{value / 48:0.###} ms at 48 kHz",
                _ => "Reset value: 0 dB"
            };
            AutomationProperties.SetLiveSetting(_reading, AutomationLiveSetting.Off);
        }

        public override void Reset()
        {
            Suspend();
            foreach (var control in Controls) control.SetValue(control.DefaultValue);
            VariantSelector.SelectedIndex = 0;
            ShowVariant();
        }

        public override bool TryCancel() => base.TryCancel() || Controls.Any(control => control.TryCancelPracticeInteraction());

        public override void Suspend()
        {
            foreach (var control in Controls) control.SuspendPracticeInteraction();
            base.Suspend();
        }
    }

    internal sealed class ButtonsLesson : Lesson
    {
        private readonly TextBlock _state = HelpLessonUi.Text("");
        private readonly Grid _modeArea = new();
        private readonly MenuItem _reduceMotion;
        private readonly MenuItem _examplePan;
        internal Button MuteButton { get; }
        internal Button BypassButton { get; }
        internal Button MonoButton { get; }
        internal Button MenuButton { get; }
        internal ComboBox DeviceSelector { get; }
        internal ComboBox ModeSelector { get; }
        internal ContextMenu ExampleMenu { get; }
        internal RotaryValueControl Amount { get; }
        internal RotaryValueControl Ratio { get; }
        internal bool Muted { get; private set; }
        internal bool Bypassed { get; private set; }
        internal bool Mono { get; private set; }

        internal ButtonsLesson()
        {
            MuteButton = HelpLessonUi.Button("Mute", () => { Muted = !Muted; RefreshState(); }, "Silence or restore an input or output.");
            BypassButton = HelpLessonUi.Button("Bypass", () => { Bypassed = !Bypassed; RefreshState(); }, "Skip this node's processing while audio passes through.");
            // Match the node's Bypass button: hover must keep its state fill.
            BypassButton.Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                  <Border x:Name="Face" CornerRadius="3" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                    <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource ConsoleTextBrush}"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource ConsoleFocusBrush}"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.8"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
                """);
            MonoButton = HelpLessonUi.Button("Mono", () => { Mono = !Mono; RefreshState(); }, "Combine left and right into one centred signal.");
            foreach (var button in new[] { MuteButton, BypassButton, MonoButton })
            {
                button.MinWidth = 90;
                button.BorderThickness = new Thickness(1);
                button.Template = BypassButton.Template;
            }
            Root.Children.Add(HelpLessonUi.Section("States", "Click to switch on or off.",
                ButtonRow(MuteButton, "Silence an input or output."),
                ButtonRow(BypassButton, "Skip a node's processing while audio passes through."),
                ButtonRow(MonoButton, "Combine left and right into one centred signal."), _state));

            DeviceSelector = Choice("Example audio device", "Practice microphone", "Practice headset", "Unavailable microphone");
            if (DeviceSelector.Items[2] is ComboBoxItem unavailable)
            {
                unavailable.IsEnabled = false;
                unavailable.ToolTip = "Unavailable devices cannot be selected. A saved assignment stays visible while disconnected.";
                ToolTipService.SetShowOnDisabled(unavailable, true);
                AutomationProperties.SetHelpText(unavailable, (string)unavailable.ToolTip);
            }
            DeviceSelector.SelectionChanged += (_, _) => RefreshState();
            ModeSelector = Choice("Example ducking mode", "Static ducking", "Dynamic ducking");
            Amount = new RotaryValueControl("Duck amount", 0, 60, .1, "dB", 18, _ => RefreshState(), compact: true, primary: true,
                description: "The fixed reduction applied when a Static ducking trigger exceeds its threshold.");
            Ratio = new RotaryValueControl("Ratio", 1, 20, .1, ":1", 4, _ => RefreshState(), compact: true, primary: true,
                description: "How strongly Dynamic ducking reduces audio above its trigger threshold.");
            _modeArea.Width = 136; _modeArea.HorizontalAlignment = HorizontalAlignment.Left;
            _modeArea.Margin = new Thickness(92, 4, 0, 0);
            _modeArea.Children.Add(Amount); _modeArea.Children.Add(Ratio);
            ModeSelector.SelectionChanged += (_, _) => ShowMode();
            Root.Children.Add(HelpLessonUi.Section("Dropdowns", "Changing mode shows a different control; each mode keeps its value.",
                SelectorRow("Device", DeviceSelector), SelectorRow("Mode", ModeSelector), _modeArea,
                HelpLessonUi.Hint("Alt+Down opens the list. Arrow keys choose, Enter confirms, Escape closes.")));

            ExampleMenu = new ContextMenu { DataContext = new object() };
            Menus.Add(ExampleMenu);
            var preferences = new MenuItem { Header = "Preferences", ToolTip = "Open a submenu. Checked items are enabled." };
            _reduceMotion = ExamplePreference("Reduce meter animation", "Use less movement in meters.");
            _examplePan = ExamplePreference("Right-click drag to pan", "Drag empty canvas with the right button to pan; release without dragging for its menu.");
            preferences.Items.Add(_reduceMotion); preferences.Items.Add(_examplePan);
            ExampleMenu.Items.Add(preferences);
            ExampleMenu.Items.Add(new Separator());
            var reset = new MenuItem { Header = "Reset buttons", ToolTip = "Turn Mute, Bypass and Mono off." };
            reset.Click += (_, _) => { Muted = Bypassed = Mono = false; RefreshState(); };
            ExampleMenu.Items.Add(reset);
            var disabled = new MenuItem { Header = "Unavailable action", IsEnabled = false,
                ToolTip = "This action needs a connected device. Its disabled state prevents an unavailable operation." };
            ToolTipService.SetShowOnDisabled(disabled, true);
            AutomationProperties.SetHelpText(disabled, (string)disabled.ToolTip);
            ExampleMenu.Items.Add(disabled);
            MenuHoverHelp.Attach(ExampleMenu);
            MenuButton = HelpLessonUi.Button("Example menu", () =>
            {
                ExampleMenu.PlacementTarget = MenuButton;
                ExampleMenu.Placement = PlacementMode.Bottom;
                ExampleMenu.IsOpen = true;
            });
            Root.Children.Add(HelpLessonUi.Section("Menus", "Open Preferences and change a checkmark. Hover an item for its help.", MenuButton,
                HelpLessonUi.Hint("Arrow keys navigate. Ctrl+Shift+F10 toggles help for the focused item. Escape closes the menu.")));
            Reset();
        }

        private MenuItem ExamplePreference(string title, string help)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, StaysOpenOnClick = true, ToolTip = help };
            AutomationProperties.SetHelpText(item, help);
            item.Click += (_, _) => RefreshState();
            return item;
        }

        private void ShowMode()
        {
            bool dynamic = ModeSelector.SelectedIndex == 1;
            (dynamic ? Amount : Ratio).SuspendPracticeInteraction();
            Amount.Visibility = dynamic ? Visibility.Collapsed : Visibility.Visible;
            Ratio.Visibility = dynamic ? Visibility.Visible : Visibility.Collapsed;
            RefreshState();
        }

        private void RefreshState()
        {
            // Callbacks from controls can occur before construction is complete.
            if (MuteButton is null || BypassButton is null || MonoButton is null) return;
            MuteButton.Content = Muted ? "Muted" : "Mute";
            StateButton(MuteButton, Muted, "Mute");
            StateButton(BypassButton, Bypassed, "Bypass");
            StateButton(MonoButton, Mono, "Mono", "ConsoleABrush");
            _state.Text = $"Mute {(Muted ? "on" : "off")}; Bypass {(Bypassed ? "on" : "off")}; Mono {(Mono ? "on" : "off")}.";
            if (_reduceMotion is not null && _examplePan is not null)
                _state.Text += $"\nReduced motion {(_reduceMotion.IsChecked ? "on" : "off")}; right-drag pan {(_examplePan.IsChecked ? "on" : "off")}.";
            AutomationProperties.SetLiveSetting(_state, AutomationLiveSetting.Polite);
        }

        public override void Reset()
        {
            Suspend();
            Muted = Bypassed = Mono = false;
            _reduceMotion.IsChecked = _examplePan.IsChecked = false;
            DeviceSelector.SelectedIndex = ModeSelector.SelectedIndex = 0;
            Amount.SetValue(Amount.DefaultValue); Ratio.SetValue(Ratio.DefaultValue);
            ShowMode(); RefreshState();
        }

        public override bool TryCancel() => base.TryCancel() || Amount.TryCancelPracticeInteraction() || Ratio.TryCancelPracticeInteraction();

        public override void Suspend()
        {
            Amount.SuspendPracticeInteraction(); Ratio.SuspendPracticeInteraction();
            base.Suspend();
        }
    }

    internal sealed class SearchLesson : Lesson
    {
        private readonly TextBlock _selection = HelpLessonUi.Hint("No node selected.");
        internal NodeSearchView SearchView { get; }
        internal GraphConfiguration Fixture { get; } = new()
        {
            Nodes =
            [
                new() { Id = "help-mic", Kind = "microphone", Name = "Voice", Device = new() { Id = "example-microphone", Name = "Practice microphone", Channel = -1 } },
                new() { Id = "help-music", Kind = "appInput", Name = "Music", App = new() { ExecutablePath = "practice-only", Name = "Example music app" } },
                new() { Id = "help-eq", Kind = "eq", Name = "Voice EQ" },
                new() { Id = "help-duck", Kind = "ducking", Name = "Music ducking" },
                new() { Id = "help-mix", Kind = "mix", Name = "Main mix" },
                new() { Id = "help-out", Kind = "output", Name = "Headphones", Device = new() { Id = "example-headphones", Name = "Practice headset", Channel = -1 } }
            ]
        };
        internal string? ChosenNodeId { get; private set; }

        internal SearchLesson()
        {
            SearchView = new NodeSearchView(ChooseExample, ClearSearch) { Height = 300, DataContext = new object() };
            // Keep the search behavior while removing its standalone-dialog
            // heading and inset from this already titled Help topic.
            if (SearchView.Content is Grid searchLayout)
            {
                searchLayout.Margin = new Thickness(0);
                searchLayout.RowDefinitions[0].Height = new GridLength(0);
                foreach (var heading in searchLayout.Children.OfType<TextBlock>().Where(item => Grid.GetRow(item) == 0))
                    heading.Visibility = Visibility.Collapsed;
                foreach (var actions in searchLayout.Children.OfType<StackPanel>().Where(item => Grid.GetRow(item) == 3))
                {
                    actions.HorizontalAlignment = HorizontalAlignment.Left;
                    actions.Margin = new Thickness(0, 8, 0, 0);
                }
            }
            SearchView.SearchBox.Margin = new Thickness(0, 0, 0, 8);
            SearchView.CloseButton.Content = "Clear search";
            AutomationProperties.SetName(SearchView.CloseButton, "Clear practice search");
            AutomationProperties.SetName(SearchView, "Practice finding an example node");
            AutomationProperties.SetHelpText(SearchView.GoButton, "Select the highlighted sample node.");
            Root.Children.Add(HelpLessonUi.Section("", "Search Voice, EQ, headset or music in these six sample nodes.",
                SearchView, _selection));
            Root.Children.Add(HelpLessonUi.Section("Search controls", "Names, node types, devices and apps are searchable. Multiple words narrow the results.",
                Instruction("Up / Down", "Choose a result."),
                Instruction("Enter / double-click", "Select the result, or use Go to node."),
                Instruction("Ctrl+F", "Find a node in the workspace and frame it."),
                Instruction("Shift+A / F3", "Search the node library to add a new node.")));
            Reset();
        }

        internal bool ChooseExample(string id)
        {
            var node = Fixture.Nodes.FirstOrDefault(item => item.Id == id);
            if (node is null) return false;
            ChosenNodeId = node.Id;
            _selection.Text = $"Selected: {node.Name}";
            AutomationProperties.SetLiveSetting(_selection, AutomationLiveSetting.Polite);
            return true;
        }

        private void ClearSearch()
        {
            SearchView.SearchBox.Clear();
            ChosenNodeId = null;
            _selection.Text = "No node selected.";
        }

        public override void Reset()
        {
            Suspend();
            ClearSearch();
            SearchView.Apply(Fixture);
        }

        public override bool TryCancel()
        {
            if (base.TryCancel()) return true;
            if (SearchView.SearchBox.Text.Length == 0) return false;
            ClearSearch(); return true;
        }
    }

    internal sealed class FeedbackLesson : Lesson
    {
        private readonly TextBlock _status = HelpLessonUi.Text("", bold: true);
        private readonly TextBlock _explanation = HelpLessonUi.Hint("");
        private readonly TextBlock _detector = HelpLessonUi.Text("");
        private readonly TextBlock _peak = HelpLessonUi.Text("");
        private readonly Border _reduction = new() { Height = 3, HorizontalAlignment = HorizontalAlignment.Left };
        internal ComboBox StateSelector { get; }
        internal StereoNodeMeter Meter { get; }
        internal MeterPeakDisplay PeakDisplay { get; }

        internal FeedbackLesson()
        {
            StateSelector = Choice("Illustrative feedback state", "Healthy level", "Clipping", "Muted", "Device unavailable", "Processing with reduction", "Bypassed", "Waiting for trigger");
            Meter = new StereoNodeMeter { Height = 140, Width = 28 };
            Meter.FreezePracticeSnapshot();
            PeakDisplay = new MeterPeakDisplay(Meter) { Height = 140, Margin = new Thickness(0, 0, 20, 12) };
            AutomationProperties.SetName(PeakDisplay, "Illustrative audio peaks, no live audio");
            var sample = new Grid();
            sample.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            sample.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            sample.Children.Add(PeakDisplay);
            var readouts = new StackPanel();
            readouts.Children.Add(_status); readouts.Children.Add(_detector); readouts.Children.Add(_peak);
            var strip = new Border { Height = 3, Width = 180, Margin = new Thickness(0, 4, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left, Child = _reduction };
            strip.SetResourceReference(Border.BackgroundProperty, "ConsoleMeterUnlitBrush");
            _reduction.SetResourceReference(Border.BackgroundProperty, "ConsoleABrush");
            readouts.Children.Add(strip); readouts.Children.Add(_explanation);
            Grid.SetColumn(readouts, 1); sample.Children.Add(readouts);
            Root.Children.Add(HelpLessonUi.Section("", "Static sample values.", SelectorRow("Display", StateSelector), sample));
            Root.Children.Add(HelpLessonUi.Section("Reading the display", "",
                Instruction("Meter lanes", "Separate left and right sample peaks."),
                Instruction("Yellow / red", "Above −12 dBFS / above −3 dBFS."),
                Instruction("Lit top cap", "An overload occurred. Holds for two seconds, then fades for one second."),
                Instruction("Peak tooltip", "Highest observed levels. Clicking the meter does not clear them."),
                Instruction("Detector / reduction", "Read-only feedback from dynamics processing."),
                Instruction("Device warning", "Hover the highlighted device name for recovery guidance. Silence alone is not a missing-device warning.")));
            StateSelector.SelectionChanged += (_, _) => ShowState();
            Reset();
        }

        private void ShowState()
        {
            int selected = Math.Max(0, StateSelector.SelectedIndex);
            bool quiet = selected is 2 or 3;
            double left = selected == 1 ? 1 : Math.Pow(10, -18 / 20.0);
            double right = selected == 1 ? .75 : Math.Pow(10, -12 / 20.0);
            // Reset each isolated illustration rather than retaining another
            // example state's peak history. The practice flag stops all timers.
            Meter.Update(0, 0, 2, false, true, true, 0);
            Meter.Update(left, right, 2, selected == 1, quiet, true, 0);
            _status.Text = selected switch
            {
                1 => "Clipping · lower the level",
                2 => "Muted",
                3 => "Practice microphone · disconnected",
                4 => "Processing · 6.0 dB reduction",
                5 => "Bypassed · audio passes through",
                6 => "Waiting for trigger",
                _ => "Connected"
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty,
                selected is 1 or 3 ? "ConsoleAlertTextBrush" : "ConsoleTextBrush");
            _detector.Text = selected == 4 ? "Detector −24.0 dBFS\nThreshold −30.0 dBFS" : selected == 6 ? "Trigger — dBFS\nReduction 0.0 dB" : "Stereo";
            _peak.Text = quiet ? "No peak readings." : selected == 1 ? "Left 0.0 dBFS; overload detected" : "Left −18.0 dBFS; right −12.0 dBFS";
            _reduction.Width = selected == 4 ? 54 : 0;
            _explanation.Text = selected switch
            {
                1 => "The lit cap confirms an overload. A reading of 0 dBFS alone does not prove clipping. This static sample holds the cap on.",
                2 => "Muted deliberately; the device remains connected.",
                3 => "The saved assignment stays in place until the device reconnects or you choose another.",
                4 => "Above threshold, with 6 dB of reduction. The strip shows the reduction amount.",
                5 => "Processing and level adjustment are skipped. Settings and connections remain in place.",
                6 => "Connect a source to the Trigger socket. A missing or silent trigger releases reduction toward zero.",
                _ => "Hover the meter to inspect the highest levels recorded for each channel."
            };
            _status.ToolTip = NodeToolTip.Create("Sample status", _status.Text, _explanation.Text);
            _detector.ToolTip = NodeToolTip.Create("Sample dynamics reading", _detector.Text);
            AutomationProperties.SetHelpText(_status, _explanation.Text);
        }

        public override void Reset()
        {
            Suspend(); StateSelector.SelectedIndex = 0; ShowState();
        }
    }
}
