namespace Chnl;

public enum Status
{
    Success,
    Closed
}

public readonly record struct WriteResult(Status Status)
{
    public bool IsSuccess => Status == Status.Success;
    public void EnsureSuccess() => Status.EnsureSuccess();
    
    public static WriteResult Success() => new(Status.Success);
    public static WriteResult Closed() => new(Status.Closed);
}

public readonly record struct ReadResult<T>(T? Item, Status Status)
{
    public bool IsSuccess => Status == Status.Success;
    public void EnsureSuccess() => Status.EnsureSuccess();
    
    public static ReadResult<T> Success(T? item) => new(item, Status.Success);
    public static ReadResult<T> Closed(T? item) => new(item, Status.Closed);
}

public static class StatusExtensions
{
    public static void EnsureSuccess(this Status status)
    {
        switch (status)
        {
            case Status.Success:
                break;
            case Status.Closed:
                throw new ChannelClosedException();
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}