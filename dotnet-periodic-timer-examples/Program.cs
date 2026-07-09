using System.Text;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Ứng dụng bắt đầu. Nhấn Ctrl+C để dừng.");

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\nĐang dừng ứng dụng...");
            cts.Cancel();
            e.Cancel = true;
        };

        try
        {
            await RunTimerAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Ứng dụng đã dừng thành công.");
        }
    }

    private static async Task RunTimerAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer kích hoạt mỗi 1 giây
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        // Semaphore chỉ cho phép 1 tác vụ chạy tại một thời điểm
        using SemaphoreSlim semaphore = new(1, 1);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Kiểm tra xem có đang chạy tác vụ nào không.
            // Nếu có, WaitAsync(0) sẽ trả về false ngay lập tức.
            if (await semaphore.WaitAsync(0, stoppingToken))
            {
                try
                {
                    await DoSomethingHeavy();
                }
                finally
                {
                    // Giải phóng chốt để lần tick tiếp theo có thể chạy
                    semaphore.Release();
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ trước vẫn đang chạy, bỏ qua lần này.");
            }
        }
    }

    private static async Task DoSomethingHeavy()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Bắt đầu tác vụ nặng (mất 3 giây)...");

        // Giả lập tác vụ mất 3 giây
        await Task.Delay(3000);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ nặng đã xong.");
    }
}