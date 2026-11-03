using System;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace OpenAITextShortcuts
{
    internal sealed class AutoPaste
    {
        private AutomationElement element;
        private TextPatternRange range;
        private string original;
        private static string Normalize(string text) { return text.Replace("\r\n", "\n").Replace("\r", "\n"); }

        private static TextPatternRange EditableSelection(AutomationElement focused)
        {
            if (focused == null || !focused.Current.IsEnabled || focused.Current.IsPassword) return null;
            object value;
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out value) && ((ValuePattern)value).Current.IsReadOnly) return null;
            object pattern;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
            TextPatternRange[] selections = ((TextPattern)pattern).GetSelection();
            if (selections.Length != 1) return null;
            object readOnly = selections[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute);
            if (!(readOnly is bool) || (bool)readOnly) return null;
            return selections[0];
        }

        internal static async Task<AutoPaste> CaptureAsync(string original)
        {
            var task = Task.Run(() => {
                try
                {
                    return CaptureElement(AutomationElement.FocusedElement, original);
                }
                catch { return null; }
            });
            return await Task.WhenAny(task, Task.Delay(500)) == task ? await task : null;
        }

        internal static AutoPaste CaptureElement(AutomationElement focused, string original)
        {
            var selected = EditableSelection(focused);
            if (selected == null || String.IsNullOrEmpty(original) || Normalize(selected.GetText(-1)) != Normalize(original)) return null;
            return new AutoPaste { element = focused, range = selected.Clone(), original = original };
        }

        internal bool MatchesElement(AutomationElement focused)
        {
            if (!Automation.Compare(element, focused)) return false;
            var selected = EditableSelection(focused);
            return selected != null && Normalize(selected.GetText(-1)) == Normalize(original)
                && selected.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.Start) == 0
                && selected.CompareEndpoints(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.End) == 0;
        }

        internal async Task<bool> StillSelectedAsync()
        {
            var task = Task.Run(() => {
                try
                {
                    return MatchesElement(AutomationElement.FocusedElement);
                }
                catch { return false; }
            });
            return await Task.WhenAny(task, Task.Delay(500)) == task && await task;
        }
    }
}
