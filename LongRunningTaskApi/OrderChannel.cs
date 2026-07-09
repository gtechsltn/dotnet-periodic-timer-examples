using Microsoft.Extensions.Options;

using System.Threading.Channels;

public class OrderChannel
{
    private readonly Channel<string> _channel;

    public OrderChannel(IOptions<OrderProcessingOptions> options)
    {
        // Channel với dung lượng giới hạn (Bounded) để tránh tràn bộ nhớ nếu đơn hàng quá nhiều.
        // Dung lượng lấy từ appsettings.json (OrderProcessing:ChannelCapacity) thay vì hard-code.
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.Value.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait // Nếu channel đầy, Producer sẽ đợi cho đến khi có chỗ trống
        });
    }

    public ChannelWriter<string> Writer => _channel.Writer;
    public ChannelReader<string> Reader => _channel.Reader;
}
