Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
$code = @'
using System;
using System.Windows.Automation;
public static class Nav2 {
  public static string Probe() {
    var root = AutomationElement.RootElement;
    var c = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
    var col = root.FindAll(TreeScope.Children, c);
    var sb = new System.Text.StringBuilder();
    foreach (AutomationElement e in col) { try { sb.Append("[" + e.Current.Name + "]"); } catch {} }
    return sb.ToString();
  }
  public static bool ClickButton(string title) {
    var root = AutomationElement.RootElement;
    var c = new AndCondition(
      new PropertyCondition(AutomationElement.NameProperty, title),
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
    AutomationElement el = root.FindFirst(TreeScope.Descendants, c);
    if (el == null) return false;
    object pat = null;
    if (el.TryPattern(PatternInterface.Invoke, out pat)) { ((IInvokeProvider)pat).Invoke(); return true; }
    return false;
  }
}
'@
try { Add-Type -TypeDefinition $code -ReferencedAssemblies UIAutomationClient,UIAutomationTypes -ErrorAction Stop; Write-Host "compiled" } catch { Write-Host "COMPILE FAIL:"; $_.Exception | Format-List -Force | Out-String -Width 200 | Write-Host }
