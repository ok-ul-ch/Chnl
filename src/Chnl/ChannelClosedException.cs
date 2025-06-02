namespace Chnl;

public class ChannelClosedException()
    : InvalidOperationException("Operation is not permitted. The channel has already been completed.");
