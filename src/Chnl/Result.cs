namespace Chnl;

public readonly struct Void;

public readonly record struct Result<T>(T? Item, Status Status)
{
    public bool IsSuccess => Status == Status.Success;

    public static Result<T> Success(T? item) => new(item, Status.Success);
    public static Result<T> Closed(T? item) => new(item, Status.Closed);

    public void EnsureSuccess() => Status.EnsureSuccess();

    public T? Unwrap()
    {
        EnsureSuccess();
        return Item;
    }
}