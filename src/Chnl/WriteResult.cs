namespace Chnl;

public readonly record struct WriteResult(Status Status)
{
    public bool IsSuccess => Status == Status.Success;
    public void EnsureSuccess() => Status.EnsureSuccess();
    
    public static WriteResult Success() => new(Status.Success);
    public static WriteResult Closed() => new(Status.Closed);
}