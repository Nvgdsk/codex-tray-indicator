using System.Drawing;
using System.IO.Ports;

namespace CodexTray;

internal interface IUsbScreenConnection : IDisposable
{
    void Show(TrayState state, ScreenOrientation orientation, CancellationToken cancellationToken,
        long animationStep = 0, long mascotFrame = 0, bool mascotEnabled = true, WeeklyLimit? weeklyLimit = null);
    void CheckConnection();
    void TurnOff();
}

internal interface IUsbScreenTransport : IDisposable
{
    void Write(byte[] data, int offset, int count);
}

// Revision A: six-byte packed coordinates, followed by row-major RGB565 LE pixels.
internal sealed class TuringScreenConnection(IUsbScreenTransport transport) : IUsbScreenConnection
{
    private ScreenOrientation? _orientation;
    private TrayState? _lastState;
    private long _lastAnimationStep;
    private bool _lastMascotEnabled;
    private WeeklyLimit? _lastWeeklyLimit;

    public void Show(TrayState state, ScreenOrientation orientation, CancellationToken cancellationToken,
        long animationStep = 0, long mascotFrame = 0, bool mascotEnabled = true, WeeklyLimit? weeklyLimit = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new UsbScreenOptions(Orientation: orientation);
        if (_orientation != orientation)
        {
            var setup = new byte[16];
            setup[5] = 121;
            setup[6] = (byte)(100 + (int)orientation);
            setup[7] = (byte)(options.Width >> 8);
            setup[8] = (byte)options.Width;
            setup[9] = (byte)(options.Height >> 8);
            setup[10] = (byte)options.Height;
            Write(setup);
            Write(Command(109));
            Write(Command(110, 191)); // 25% brightness; Revision A uses an inverted scale.
            _orientation = orientation;
            _lastState = null;
        }

        if (mascotEnabled && _lastMascotEnabled && _lastState == state && _lastAnimationStep == animationStep &&
            _lastWeeklyLimit == weeklyLimit)
        {
            using var mascot = UsbMascotRenderer.Render(state, mascotFrame);
            SendBitmap(mascot, UsbScreenRenderer.MascotBounds(orientation, animationStep).Location, options.Width, cancellationToken);
        }
        else
        {
            using var bitmap = UsbScreenRenderer.Render(state, orientation, animationStep, mascotFrame, mascotEnabled, weeklyLimit);
            SendBitmap(bitmap, Point.Empty, options.Width, cancellationToken);
        }
        _lastState = state;
        _lastAnimationStep = animationStep;
        _lastMascotEnabled = mascotEnabled;
        _lastWeeklyLimit = weeklyLimit;
    }

    private void SendBitmap(Bitmap bitmap, Point origin, int screenWidth, CancellationToken cancellationToken)
    {
        byte[] pixels = UsbScreenRenderer.ToRgb565(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        Write(Command(197, origin.X, origin.Y, origin.X + bitmap.Width - 1, origin.Y + bitmap.Height - 1));
        int chunkSize = screenWidth * 8;
        // Finish a started frame before accepting cancellation, so control commands
        // cannot be mistaken for missing pixel bytes by the device.
        for (int offset = 0; offset < pixels.Length; offset += chunkSize)
        {
            transport.Write(pixels, offset, Math.Min(chunkSize, pixels.Length - offset));
        }
    }

    public void TurnOff() => Write(Command(108));

    public void CheckConnection() => Write(Command(109));

    public void Dispose() => transport.Dispose();

    private void Write(byte[] data) => transport.Write(data, 0, data.Length);

    private static byte[] Command(byte command, int x = 0, int y = 0, int endX = 0, int endY = 0) =>
    [
        (byte)(x >> 2),
        (byte)(((x & 3) << 6) | (y >> 4)),
        (byte)(((y & 15) << 4) | (endX >> 6)),
        (byte)(((endX & 63) << 2) | (endY >> 8)),
        (byte)endY,
        command,
    ];
}

internal sealed class SerialScreenTransport : IUsbScreenTransport
{
    private readonly SerialPort _port;

    public SerialScreenTransport(string portName)
    {
        _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 1000,
            WriteTimeout = 1000,
        };
        try
        {
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        catch
        {
            _port.Dispose();
            throw;
        }
    }

    public void Write(byte[] data, int offset, int count) => _port.Write(data, offset, count);

    public void Dispose() => _port.Dispose();
}
