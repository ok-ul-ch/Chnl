namespace Chnl;

public readonly struct Void;

public readonly record struct Result<T>
{
    private readonly T? _item;
    public readonly Status Status;

    private Result(T? item, Status status)
    {
        _item = item;
        Status = status;
    }


    public bool IsSuccess => Status == Status.Success;

    public static Result<T> Success(T? item) => new(item, Status.Success);
    public static Result<T> Closed() => new(default, Status.Closed);

    public void EnsureSuccess() => Status.EnsureSuccess();

    public T? Unwrap()
    {
        EnsureSuccess();
        return _item;
    }
}