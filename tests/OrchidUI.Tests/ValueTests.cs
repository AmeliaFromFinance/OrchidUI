using System;
using System.Collections.Generic;
using System.Linq;
using OrchidUI.Sandbox;
using Xunit;

namespace OrchidUI.Tests
{
    // Each field kind normalises any input to the form it stores.
    public class NormaliseTests
    {
        private static Orchid NewOrchid() { return new Orchid(new FakeCph(), "NORMALISE", "1.0.0"); }

        private static object N(Field f, object v, string key = null) { return f.Normalize(key ?? f.Key, v); }

        [Fact]
        public void Text_password_and_response_boxes_store_text()
        {
            var t = NewOrchid().Tab("T");
            Assert.Equal("", N(t.AddTextbox("A", "", "a"), null));
            Assert.Equal("12", N(t.AddPasswordBox("B", "", "b"), 12));
            Assert.Equal("true", N(t.AddResponseBox("C", "", "c"), true));
        }

        [Fact]
        public void Toggles_store_bools()
        {
            var t = NewOrchid().Tab("T");
            var f = t.AddToggleSwitch("A", "", "a", true);
            Assert.Equal(true, N(f, "yes"));
            Assert.Equal(false, N(f, "nonsense"));
            Assert.Equal(false, N(f, null));
        }

        [Fact]
        public void Segmented_falls_back_to_its_default_option()
        {
            var f = NewOrchid().Tab("T").AddSegmented("A", "", "a", new[] { "L", "C", "R", "C" }, 1);
            Assert.Equal("R", N(f, "R"));
            Assert.Equal("C", N(f, "Up"));
            Assert.Equal("C", N(f, null));
        }

        [Fact]
        public void Sliders_clamp_and_round_and_keep_their_switch()
        {
            var f = NewOrchid().Tab("T").AddSliderWithToggleSwitch("A", "", "time", 1, 3600, 180, true);
            Assert.Equal(3600, N(f, 9999));
            Assert.Equal(1, N(f, -5));
            Assert.Equal(43, N(f, "42.5"));
            Assert.Equal(180, N(f, "abc"));
            Assert.Equal(false, N(f, "off", "time_enabled"));
            Assert.Equal(new[] { "time", "time_enabled" }, f.Keys.ToArray());
        }

        [Fact]
        public void Steppers_round_to_their_decimals_and_clamp()
        {
            var t = NewOrchid().Tab("T");
            var dec = t.AddDecimalStepper("A", "", "m", 0.05, 10, 0.05, 0.5);
            Assert.Equal(0.33, N(dec, 0.3333));
            Assert.Equal(10.0, N(dec, 99));
            Assert.Equal(0.05, N(dec, "x"));
            var integer = t.AddIntegerStepper("B", "", "n", 1, 100000, 100, 50);
            Assert.Equal(43, N(integer, 42.6));
            Assert.Equal(1, N(integer, 0));
            Assert.Equal(100, integer.DefaultFor("n"));
        }

        [Fact]
        public void Dropdowns_keep_unknown_values_and_pick_the_first_for_empty()
        {
            var o = NewOrchid();
            var f = (DropdownField)o.Tab("T").AddDropdown("A", "", "cur", new[] { "USD", "EUR" });
            Assert.Equal("EUR", N(f, "EUR"));
            Assert.Equal("USD", N(f, ""));
            Assert.Equal("XYZ", N(f, "XYZ"));
            Assert.Equal(new[] { "XYZ", "USD", "EUR" }, f.Options.ToArray());
            var empty = o.Tab("T").AddDropdown("B", "", "none", new string[0]);
            Assert.Equal("", N(empty, ""));
        }

        [Fact]
        public void Colours_become_upper_case_hex_and_drop_alpha_unless_allowed()
        {
            var t = NewOrchid().Tab("T");
            var plain = t.AddColorPicker("A", "", "c", "#F28AC9");
            Assert.Equal("#FFFFFF", N(plain, "#fff"));
            Assert.Equal("#112233", N(plain, "#80112233"));
            Assert.Equal("#F28AC9", N(plain, "not a colour"));
            var alpha = t.AddColorPicker("B", "", "d", "#CC1A1324", allowAlpha: true);
            Assert.Equal("#80112233", N(alpha, "#80112233"));
            Assert.Equal("#FF112233", N(alpha, "#112233"));
            Assert.Equal("#00000000", N(alpha, "transparent"));
        }

        [Fact]
        public void Durations_become_dd_hh_mm_ss()
        {
            var t = NewOrchid().Tab("T");
            var f = t.AddDurationPicker("A", "", "dur", 0, 6, 0, 0);
            Assert.Equal("00:06:00:00", f.DefaultFor("dur"));
            Assert.Equal("00:00:01:30", N(f, "90"));
            Assert.Equal("00:00:01:30", N(f, 90));
            Assert.Equal("01:01:00:00", N(f, "25:00:00"));
            Assert.Equal("00:00:00:00", N(f, "soon"));
            var noDays = t.AddDurationPicker("B", "", "dur2", 0, 0, 0, 0, showDays: false);
            Assert.Equal("01:01:00:00", N(noDays, "25:00:00"));
        }

        [Fact]
        public void Tables_get_a_cell_per_column_and_choice_cells_default_to_the_first_option()
        {
            var t = NewOrchid().Tab("T");
            var table = t.AddSelectionAmountTextList("Goals", "", "goals", new[] { "Subs", "Bits" }, new[] { "Subs|50|Cooking" }, "Counts", "Target", "Reward");
            var rows = (string[][])N(table, new object[] { new object[] { "", "5" }, "Bits|10|x|extra" });
            Assert.Equal(new[] { "Subs", "5", "" }, rows[0]);
            Assert.Equal(new[] { "Bits", "10", "x" }, rows[1]);
            var list = t.AddTextList("List", "", "list", new[] { "a" });
            Assert.Equal(new[] { "a", "b" }, (string[])N(list, "[\"a\",\"b\"]"));
        }

        [Fact]
        public void Multi_selects_drop_blanks_and_duplicates()
        {
            var f = NewOrchid().Tab("T").AddMultiSelect("R", "", "rewards", new[] { "x", "y" });
            Assert.Equal(new[] { "x", "y" }, (string[])N(f, new object[] { "x", "", "x", "y", null }));
        }
    }

    public class ValueConverterTests
    {
        [Fact]
        public void Loose_equality_matches_the_stored_form()
        {
            Assert.True(ValueConverter.LooseEquals(1, 1.0m));
            Assert.True(ValueConverter.LooseEquals(true, "true"));
            Assert.True(ValueConverter.LooseEquals(new[] { "a" }, new object[] { "a" }));
            Assert.False(ValueConverter.LooseEquals("1", "01"));
            Assert.False(ValueConverter.LooseEquals(null, ""));
        }

        [Theory]
        [InlineData("1:30", 90)]
        [InlineData("01:00:00", 3600)]
        [InlineData("1:00:00:01", 86401)]
        [InlineData("45", 45)]
        public void Durations_parse(string text, long seconds)
        {
            long s;
            Assert.True(DurationUtil.TryParseSeconds(text, out s));
            Assert.Equal(seconds, s);
        }

        [Theory]
        [InlineData(5400, "1h 30m")]
        [InlineData(90, "1m 30s")]
        [InlineData(172800, "2d")]
        [InlineData(0, "0s")]
        public void Humanize_formats_durations(double seconds, string text)
        {
            Assert.Equal(text, DurationUtil.Humanize(seconds));
        }
    }
}
