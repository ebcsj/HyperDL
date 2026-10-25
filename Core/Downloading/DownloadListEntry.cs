using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HyperDL.Core;

public sealed class DownloadListEntry : INotifyPropertyChanged
{
    private string _size;
    private string _progress;
    private string _speed = "0 B/s";
    private string _eta = "—";
    private string _status;
    private int _connectionCount;
    private string _targetPath;

    public DownloadListEntry(
        Guid id,
        string fileName,
        string size,
        string progress,
        string status,
        int connectionCount,
        string targetPath,
        DownloadSession? session)
    {
        Id = id;
        FileName = fileName;
        _size = size;
        _progress = progress;
        _status = status;
        _connectionCount = connectionCount;
        _targetPath = targetPath;
        Session = session;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public string FileName { get; }
    public DownloadSession? Session { get; }
    public string Size { get => _size; set => SetField(ref _size, value); }
    public string Progress { get => _progress; set => SetField(ref _progress, value); }
    public string Speed { get => _speed; set => SetField(ref _speed, value); }
    public string Eta { get => _eta; set => SetField(ref _eta, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public int ConnectionCount { get => _connectionCount; set => SetField(ref _connectionCount, value); }
    public string TargetPath { get => _targetPath; set => SetField(ref _targetPath, value); }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
