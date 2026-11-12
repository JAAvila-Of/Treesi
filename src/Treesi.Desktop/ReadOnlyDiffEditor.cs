using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Document;

namespace Treesi.Desktop;

public sealed class ReadOnlyDiffEditor : TextEditor
{
    protected override Type StyleKeyOverride => typeof(TextEditor);
    public ReadOnlyDiffEditor() : base(new DiffTextArea())
    {
        IsReadOnly = true;
        Options.AcceptsTab = false;
    }

    private sealed class DiffTextArea : TextArea
    {
        protected override Type StyleKeyOverride => typeof(TextArea);
        protected override AutomationPeer OnCreateAutomationPeer() => new ReadOnlyDiffPeer(this);
    }

    private sealed class ReadOnlyDiffPeer : ControlAutomationPeer, IValueProvider
    {
        private readonly DiffTextArea _owner;
        private TextDocument? _document;
        public ReadOnlyDiffPeer(DiffTextArea owner) : base(owner)
        {
            _owner = owner;
            TrackDocument();
            owner.DocumentChanged += (_, _) => { TrackDocument(); ValueChanged(this, EventArgs.Empty); };
        }
        private void TrackDocument()
        {
            if (_document is not null) _document.TextChanged -= ValueChanged;
            _document = _owner.Document;
            if (_document is not null) _document.TextChanged += ValueChanged;
        }
        private void ValueChanged(object? sender, EventArgs args) => RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, null, Value);
        public bool IsReadOnly => true;
        public string Value => _owner.Document?.Text ?? "";
        public void SetValue(string? value) => throw new InvalidOperationException("The comparison is read-only.");
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    }
}
