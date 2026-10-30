using CommunityToolkit.Mvvm.Input;

namespace UEManager.UI.ViewModels;

/// <summary>A yes/no question shown in the themed overlay instead of a system message box.</summary>
public sealed partial class ConfirmViewModel(string title, string message, string confirmText, string cancelText) : LocalizedObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();

    public string Title => title;

    public string Message => message;

    public string ConfirmText => confirmText;

    public string CancelText => cancelText;

    public Task<bool> Result => result.Task;

    [RelayCommand]
    private void Accept() => result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => result.TrySetResult(false);
}
