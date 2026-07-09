using System.Threading.Channels;

public class OrderChannel
{
    // Channel với dung lượng giới hạn (Bounded) để tránh tràn bộ nhớ nếu đơn hàng quá nhiều
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait // Nếu channel đầy, Producer sẽ đợi cho đến khi có chỗ trống
    });

    public ChannelWriter<string> Writer => _channel.Writer;
    public ChannelReader<string> Reader => _channel.Reader;
}