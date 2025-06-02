namespace Chnl;

public readonly record struct ReadResult<T>(T? Item, Status Status)
{
    public bool IsSuccess => Status == Status.Success;
    public void EnsureSuccess() => Status.EnsureSuccess();
    
    public static ReadResult<T> Success(T? item) => new(item, Status.Success);
    public static ReadResult<T> Closed(T? item) => new(item, Status.Closed);
}