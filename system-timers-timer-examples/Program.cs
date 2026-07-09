using System.Text;
using System.Timers;

internal class Program
{
    // Cần khai báo static để tránh bị Garbage Collector thu hồi
    private static System.Timers.Timer _timer = default!;

    private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private static void Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Ứng dụng bắt đầu. Nhấn phím bất kỳ để dừng.");

        // 1. Khởi tạo Timer với khoảng thời gian 1 giây
        _timer = new System.Timers.Timer(1000);

        // 2. Hook vào sự kiện Elapsed
        _timer.Elapsed += OnTimerElapsed!;

        // 3. Cho phép tự lặp lại
        _timer.AutoReset = true;

        // 4. Bật timer
        _timer.Enabled = true;

        Console.ReadKey();

        // Dọn dẹp tài nguyên
        _timer.Dispose();
        _semaphore.Dispose();
    }

    private static async void OnTimerElapsed(object sender, ElapsedEventArgs e)
    {
        // Kiểm tra Semaphore để tránh chồng chéo
        if (await _semaphore.WaitAsync(0))
        {
            try
            {
                await DoSomethingHeavy();
            }
            finally
            {
                _semaphore.Release();
            }
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ trước vẫn đang chạy, bỏ qua lần này.");
        }
    }

    private static async Task DoSomethingHeavy()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000); // Giả lập công việc
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ nặng đã xong.");
    }
}