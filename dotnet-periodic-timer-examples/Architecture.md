# Architecture — dotnet-periodic-timer-examples

Console app (net10.0, không package ngoài) minh họa pattern **`System.Threading.PeriodicTimer` + `SemaphoreSlim`**: chạy tác vụ nặng định kỳ mỗi 1 giây, tự động bỏ qua tick khi tác vụ trước chưa xong, dừng sạch bằng Ctrl+C.

## Thành phần

Chỉ một file `Program.cs` với ba phần:

```
Main
  ├── CancellationTokenSource + hook Console.CancelKeyPress (Ctrl+C → Cancel, e.Cancel=true)
  └── await RunTimerAsync(cts.Token)  — catch OperationCanceledException để thoát êm

RunTimerAsync(stoppingToken)
  ├── PeriodicTimer(1s)
  ├── SemaphoreSlim(1,1)
  └── while (await timer.WaitForNextTickAsync(stoppingToken))
        ├── semaphore.WaitAsync(0) == true  → DoSomethingHeavy() → Release()
        └──                        == false → "Tác vụ trước vẫn đang chạy, bỏ qua lần này."

DoSomethingHeavy — giả lập Task.Delay(3000)
```

## Timeline thực tế (tick 1s, tác vụ 3s)

```
0s  tick #1 → chạy tác vụ (giữ chốt)
1s  tick #2 → chốt bận → bỏ qua
2s  tick #3 → chốt bận → bỏ qua
3s  tác vụ xong → Release()
4s  tick #4 → chạy tác vụ mới
```

Kết quả: không bao giờ có hai tác vụ chạy chồng nhau, không có backlog tick dồn ứ.

## Các quyết định thiết kế

**`PeriodicTimer` thay vì `Task.Delay` hoặc `System.Timers.Timer`.**

- Khác `Task.Delay` trong vòng lặp: `WaitForNextTickAsync` neo theo chu kỳ timer nên nhịp không bị trôi dần vì cộng dồn thời gian xử lý.
- Khác `System.Timers.Timer`: consumer chủ động *await* tick trong một vòng lặp async duy nhất — không có event handler `async void`, không fire-and-forget, exception nổi lên tự nhiên, và timer chỉ có một consumer nên không tự chồng chéo.
- Hủy trực tiếp: token truyền vào `WaitForNextTickAsync` — Ctrl+C ném `OperationCanceledException` ngay cả khi đang giữa hai tick.

**Skip thay vì queue.** `WaitAsync(0)` (timeout 0) là non-blocking check: bận thì bỏ qua tick này. Phù hợp cho công việc kiểu polling/heartbeat — chạy lần mới nhất là đủ, không cần bù lại các lần đã lỡ. (Nếu *không được phép* bỏ sót, xem `LongRunningTaskApi` dùng `Channel` trong repo này.)

**Ctrl+C với `e.Cancel = true`.** Chặn process bị kill ngay để `Main` kịp await vòng lặp kết thúc qua token — mô hình graceful shutdown thu nhỏ của `BackgroundService.StoppingToken`.

**`using` cho timer và semaphore** — giải phóng tài nguyên xác định khi vòng lặp thoát.

## Vị trí trong repo

Đây là bản console tối giản của pattern; `BackgroundTaskApi` là chính pattern này đưa vào `BackgroundService` trong web host (thêm giới hạn số lần chạy + timeout), còn `system-timers-timer-examples` là bản đối chứng dùng API timer cũ để so sánh.
