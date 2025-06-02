namespace Chnl;

public enum Status
{
    Success,
    Closed
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