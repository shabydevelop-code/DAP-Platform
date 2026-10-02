using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace DAP.TestCRM.Windows;

public sealed class AutomationComboBox : ComboBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new AutomationComboBoxPeer(this);
}

internal sealed class AutomationComboBoxPeer : ComboBoxAutomationPeer, IValueProvider
{
    readonly AutomationComboBox owner;

    public AutomationComboBoxPeer(AutomationComboBox owner) : base(owner) => this.owner=owner;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface==PatternInterface.Value ? this : base.GetPattern(patternInterface);

    bool IValueProvider.IsReadOnly => !owner.IsEnabled;

    string IValueProvider.Value => owner.SelectedItem?.ToString() ?? "";

    void IValueProvider.SetValue(string value)
    {
        if(!owner.IsEnabled)throw new InvalidOperationException("ComboBox is disabled.");
        var match=owner.Items.Cast<object>().FirstOrDefault(x=>string.Equals(x?.ToString(),value,StringComparison.Ordinal));
        if(match is null)throw new ArgumentException($"Unknown ComboBox value '{value}'.",nameof(value));
        owner.SelectedItem=match;
    }
}
