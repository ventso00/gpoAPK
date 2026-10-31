using System;
using System.Text;
using System.Windows.Automation;

public static class Nav {
 static AutomationElement Find(string name) {
  var r=AutomationElement.RootElement;
  var c=new AndCondition(
   new PropertyCondition(AutomationElement.NameProperty,name),
   new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
  return r.FindFirst(TreeScope.Descendants,c);
 }
 public static bool Focus(string name) {
  var e=Find(name);
  if (e==null) return false;
  try { e.SetFocus(); return true; } catch (Exception) { return false; }
 }
 public static string Dump() {
  var r=AutomationElement.RootElement;
  var c=new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button);
  var col=r.FindAll(TreeScope.Descendants,c);
  var sb=new StringBuilder();
  foreach (AutomationElement e in col) {
   string n; try { n=e.Current.Name; } catch (Exception) { n="?"; }
   if (n.Length>0) { sb.Append(" ~ "); sb.Append(n); }
  }
  return sb.ToString();
 }
}
