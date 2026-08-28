using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Mayhem;

namespace FACM.App.ViewModels;

public sealed class MayhemViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMayhemQueryService _queryService;
    private readonly bool _ownsService;
    private CancellationTokenSource? _queryCancellation;
    private string _queryText = string.Empty;
    private string _statusText = "输入英雄开始查询";
    private bool _isBusy;
    private MayhemChampionResult? _result;
    private bool _disposed;

    public MayhemViewModel(IMayhemQueryService queryService, bool ownsService = false)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _ownsService = ownsService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string QueryText
    {
        get => _queryText;
        set => SetField(ref _queryText, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanQuery));
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    public bool CanQuery => !IsBusy;
    public bool CanCancel => IsBusy;

    public MayhemChampionResult? Result
    {
        get => _result;
        private set => SetField(ref _result, value);
    }

    public async Task QueryAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy) return;

        var input = QueryText.Trim();
        if (input.Length == 0)
        {
            Result = null;
            StatusText = "请输入英雄名称或别名。";
            return;
        }

        _queryCancellation?.Dispose();
        _queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsBusy = true;
        Result = null;
        StatusText = "查询中...";

        var progress = new Progress<string>(message =>
        {
            if (!string.IsNullOrWhiteSpace(message)) StatusText = message;
        });

        try
        {
            var result = await _queryService.QueryAsync(input, progress, _queryCancellation.Token);
            Result = result;
            StatusText = result.Success
                ? "查询完成"
                : string.IsNullOrWhiteSpace(result.ErrorMessage) ? "暂时没有读取到可用数据，请稍后重试。" : result.ErrorMessage;
        }
        catch (OperationCanceledException) when (_queryCancellation.IsCancellationRequested)
        {
            Result = null;
            StatusText = "查询已取消";
        }
        catch
        {
            Result = null;
            StatusText = "查询失败，请稍后重试。";
        }
        finally
        {
            IsBusy = false;
            _queryCancellation?.Dispose();
            _queryCancellation = null;
        }
    }

    public void Cancel()
    {
        if (!IsBusy) return;
        StatusText = "正在取消...";
        _queryCancellation?.Cancel();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = null;
        if (_ownsService && _queryService is IDisposable disposable) disposable.Dispose();
    }
}
