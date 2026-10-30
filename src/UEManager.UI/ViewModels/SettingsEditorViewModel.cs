using System.ComponentModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using UEManager.Core;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>A settings file the editor offers: where it is in the game, and how the player knows it.</summary>
public sealed record SettingsFileItem(string Path, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The app's own editor for a mod's settings files: the text with simple colours, Save and Close, and a question
/// before changes are lost. It edits the copy in the game, the one the mod reads.
/// </summary>
public sealed partial class SettingsEditorViewModel : LocalizedObservableObject
{
    private readonly MainViewModel owner;
    private Encoding encoding = Encoding.UTF8;
    private DateTime? savedAt;
    private Action? afterClose;

    public SettingsEditorViewModel(MainViewModel owner, string modName, IReadOnlyList<SettingsFileItem> files)
    {
        this.owner = owner;
        ModName = modName;
        Files = files;
        selectedFile = files[0];
        Document.UndoStack.PropertyChanged += OnUndoStackChanged;
        Load(selectedFile);
    }

    public string ModName { get; }

    public IReadOnlyList<SettingsFileItem> Files { get; }

    public bool HasSeveralFiles => Files.Count > 1;

    public TextDocument Document { get; } = new();

    /// <summary>The text has changes that are not saved yet.</summary>
    public bool IsModified => !Document.UndoStack.IsOriginalFile;

    /// <summary>Another file can be chosen only without unsaved changes, so none are lost on the way.</summary>
    public bool CanSwitchFile => !IsModified;

    public string Hint => L.F($"{SelectedFile.Name} in the game folder. Most mods read it when the game starts; some notice a save at once.");

    public string AskText => L.F($"Save your changes to {SelectedFile.Name}?");

    public string? SavedText => savedAt is { } time && !IsModified ? L.F($"Saved at {time:t}.") : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(AskText))]
    private SettingsFileItem selectedFile;

    [ObservableProperty]
    private IHighlightingDefinition? highlighting;

    /// <summary>The file could not be read, so it is shown but cannot be saved over.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isReadOnly;

    /// <summary>The question about unsaved changes shows instead of the buttons.</summary>
    [ObservableProperty]
    private bool isAsking;

    [ObservableProperty]
    private string? error;

    partial void OnSelectedFileChanged(SettingsFileItem value) => Load(value);

    private void Load(SettingsFileItem file)
    {
        savedAt = null;
        Error = null;
        try
        {
            var read = TextFile.Read(file.Path);
            Document.Text = read.Text;
            encoding = read.Encoding;
            IsReadOnly = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The settings file '{file.Path}' could not be read: {ex.Message}");
            Document.Text = "";
            Error = L.F($"{file.Name} could not be read: {ex.Message}");
            IsReadOnly = true;
        }
        Document.UndoStack.ClearAll();
        Document.UndoStack.MarkAsOriginalFile();
        Highlighting = Syntax.For(file.Path);
        OnPropertyChanged(nameof(SavedText));
    }

    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UndoStack.IsOriginalFile))
            return;
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(CanSwitchFile));
        OnPropertyChanged(nameof(SavedText));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => IsModified && !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => TrySave();

    private bool TrySave()
    {
        try
        {
            TextFile.Write(SelectedFile.Path, Document.Text, encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The settings file '{SelectedFile.Path}' could not be saved: {ex.Message}");
            Error = L.F($"{SelectedFile.Name} could not be saved: {ex.Message}");
            return false;
        }
        Log.Info($"Saved the settings file '{SelectedFile.Path}'.");
        Error = null;
        savedAt = DateTime.Now;
        Document.UndoStack.MarkAsOriginalFile();
        OnPropertyChanged(nameof(SavedText));
        return true;
    }

    [RelayCommand]
    private void Close() => RequestClose(null);

    /// <summary>Closes the editor; with unsaved changes, it asks first. <paramref name="then"/> runs once it closed.</summary>
    internal void RequestClose(Action? then)
    {
        afterClose = then;
        if (IsModified && !IsReadOnly)
            IsAsking = true;
        else
            Finish();
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        if (TrySave())
            Finish();
        else
            KeepEditing();
    }

    [RelayCommand]
    private void Discard() => Finish();

    [RelayCommand]
    private void KeepEditing()
    {
        IsAsking = false;
        afterClose = null;
    }

    /// <summary>Esc: away from the question, else Close.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsAsking)
            KeepEditing();
        else
            Close();
    }

    private void Finish()
    {
        Document.UndoStack.PropertyChanged -= OnUndoStackChanged;
        owner.CloseSettingsEditor();
        afterClose?.Invoke();
    }
}
