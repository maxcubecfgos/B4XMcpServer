using System.Collections.Generic;

namespace B4XMcpServer.Engine
{
    public static class NonResumableSubs
    {
        private static readonly HashSet<string> Entries = new()
        {
            "TweenManager.Update",
            "id.InputList1",
            "BetterDialogs.CustomDialog",
            "BetterDialogs.Msgbox",
            "BetterDialogs.InputBox",
            "InputDialog.Show",
            "DateDialog.Show",
            "TimeDialog.Show",
            "ColorDialogHSV.Show",
            "ColorPickerDialog.Show",
            "NumberDialog.Show",
            "CustomDialog.Show",
            "CustomDialog2.Show",
            "CustomDialog3.Show",
            "Msgbox3.Show",
            "Msgbox3WithoutDim.Show",
            "AHViewPager.GotoPage",
            "PDFWriter.ConverseDocument",
            "UltimateListView.LoadImageAsync",
        };

        public static bool IsNonResumable(string typeName, string methodName)
        {
            return Entries.Contains($"{typeName}.{methodName}") || Entries.Contains(methodName);
        }

        public static bool ShouldSkipWaitFor(string methodFullName)
        {
            return Entries.Contains(methodFullName);
        }
    }
}
