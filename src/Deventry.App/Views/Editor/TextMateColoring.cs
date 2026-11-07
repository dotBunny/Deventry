using System.Collections.Concurrent;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

namespace Deventry.App.Views.Editor;

/// <summary>
/// The editor's syntax colors (DESIGN.md §18, "Project files"): AvaloniaEdit.TextMate's coloring transformer over
/// TextMateSharp's background tokenizer, fed by <see cref="TextMateLines"/>, with the diff view's DarkPlus and LightPlus
/// themes, so code looks the same in a reply, a diff and here. A file with a line longer than
/// <see cref="LongLine"/> isn't colored at all: the spike found coloring one slows every keystroke.
/// </summary>
internal sealed class TextMateColoring : IDisposable
{
    /// <summary>A line longer than this stays in the default color.</summary>
    public const int LongLine = 2_000;

    private readonly TextView _view;
    private readonly RegistryOptions _options;
    private readonly Registry _registry;
    private readonly LimitedColoring _transformer;
    private readonly TextMateLines _lines;
    private readonly TMModel _model;
    private readonly OnUiThread _changes;
    private bool _dark;

    private TextMateColoring(TextView view, TextDocument document, string scope, bool dark)
    {
        _view = view;
        _dark = dark;
        _options = new RegistryOptions(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        _registry = new Registry(_options);
        var grammar = _registry.LoadGrammar(scope);
        _transformer = new LimitedColoring(view);
        _transformer.SetTheme(_registry.GetTheme());
        _transformer.SetGrammar(grammar);
        _lines = new TextMateLines(document);
        _model = new TMModel(_lines);
        _model.SetGrammar(grammar);
        _transformer.SetModel(document, _model);
        _changes = new OnUiThread(_transformer);
        _model.AddModelTokensChangedListener(_changes);
        view.LineTransformers.Add(_transformer);
    }

    /// <summary>Colors <paramref name="document"/> in <paramref name="view"/> with <paramref name="scope"/>'s grammar; null when it can't be.</summary>
    public static TextMateColoring? Install(TextView view, TextDocument document, string? scope, bool dark)
    {
        if (scope is null)
        {
            return null;
        }
        try
        {
            return new TextMateColoring(view, document, scope, dark);
        }
        catch (Exception)
        {
            // A grammar that fails shows the file uncolored rather than not at all.
            return null;
        }
    }

    /// <summary>The theme follows the app's light or dark.</summary>
    public void UseDark(bool dark)
    {
        if (dark == _dark)
        {
            return;
        }
        _dark = dark;
        _registry.SetTheme(_options.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        _transformer.SetTheme(_registry.GetTheme());
        _view.Redraw();
    }

    public void Dispose()
    {
        _view.LineTransformers.Remove(_transformer);
        _model.RemoveModelTokensChangedListener(_changes);
        _model.Dispose();
        _lines.Dispose();
        _transformer.SetModel(null, null);
        _transformer.Dispose();
    }

    /// <summary>
    /// Hands the tokenizer's changes to the coloring on the UI thread, through the dispatcher of the thread it was made
    /// on, as many as came since the last time in one go. The tokenizer reports from a thread of its own, and the coloring
    /// asks for <see cref="Dispatcher.UIThread"/> to redraw the lines that changed: the headless test session resets that
    /// between tests, and a report after a test ended made it the tokenizer's, failing the next test (as
    /// <see cref="Services.AvaloniaUiDispatcher"/> says).
    /// </summary>
    internal sealed class OnUiThread(IModelTokensChangedListener coloring) : IModelTokensChangedListener
    {
        private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
        private readonly ConcurrentQueue<ModelTokensChangedEvent> _pending = new();
        private int _posted;

        public void ModelTokensChanged(ModelTokensChangedEvent e)
        {
            _pending.Enqueue(e);
            if (Interlocked.Exchange(ref _posted, 1) == 0)
            {
                _dispatcher.Post(Deliver);
            }
        }

        private void Deliver()
        {
            // Before taking them, so one that comes meanwhile is posted again rather than left waiting.
            Volatile.Write(ref _posted, 0);
            while (_pending.TryDequeue(out var e))
            {
                coloring.ModelTokensChanged(e);
            }
        }
    }

    /// <summary>TextMate's coloring, leaving lines over <see cref="LongLine"/> in the default color.</summary>
    private sealed class LimitedColoring(TextView view) : TextMateColoringTransformer(view, null)
    {
        protected override void TransformLine(DocumentLine line, ITextRunConstructionContext context)
        {
            if (line.Length > LongLine)
            {
                return;
            }
            base.TransformLine(line, context);
        }
    }
}
