using System.Windows;

namespace FACM.MachineCat3DPrototype;

internal readonly record struct DesktopMotionFrame(
    double Left,
    double Top,
    MotionState State,
    double FacingYaw,
    double Speed,
    double TargetLeft,
    bool IsRunning);

internal sealed class DesktopMotionController
{
    private const double SideInset = 14d;
    private const double GroundVisualOffset = 48d;
    private const double WalkSpeed = 92d;
    private const double RunSpeed = 168d;
    private const double WalkAcceleration = 250d;
    private const double RunAcceleration = 410d;
    private const double ArrivalDistance = 175d;
    private const double RightYaw = -72d;
    private const double LeftYaw = 72d;

    private readonly Random _random;
    private bool _initialized;
    private bool _travelling;
    private bool _turning;
    private bool _running;
    private double _targetLeft;
    private double _velocity;
    private double _facingYaw = RightYaw;
    private double _desiredYaw = RightYaw;
    private double _restUntil;
    private double _turnUntil;

    public DesktopMotionController(int? deterministicSeed = null)
    {
        _random = deterministicSeed.HasValue ? new Random(deterministicSeed.Value) : new Random();
    }

    public double TargetLeft => _targetLeft;
    public double Speed => Math.Abs(_velocity);

    public void Reset(Rect workArea, double windowWidth, double windowHeight, double currentLeft, double now)
    {
        var (minLeft, maxLeft) = HorizontalBounds(workArea, windowWidth);
        _targetLeft = Math.Clamp(currentLeft, minLeft, maxLeft);
        _velocity = 0d;
        _travelling = false;
        _turning = false;
        _running = false;
        _restUntil = now + 0.35d;
        _facingYaw = currentLeft > (minLeft + maxLeft) * 0.5d ? LeftYaw : RightYaw;
        _desiredYaw = _facingYaw;
        _initialized = true;
    }

    public DesktopMotionFrame Step(
        double deltaTime,
        double now,
        Rect workArea,
        double windowWidth,
        double windowHeight,
        double currentLeft)
    {
        if (!_initialized)
            Reset(workArea, windowWidth, windowHeight, currentLeft, now);

        deltaTime = Math.Clamp(deltaTime, 1d / 1000d, 0.05d);
        var (minLeft, maxLeft) = HorizontalBounds(workArea, windowWidth);
        var left = Math.Clamp(currentLeft, minLeft, maxLeft);
        var top = Math.Max(workArea.Top, workArea.Bottom - windowHeight + GroundVisualOffset);

        if (!_travelling && !_turning && now >= _restUntil)
            ChooseDestination(left, minLeft, maxLeft, now);

        MotionState state;

        if (_turning)
        {
            _velocity = Approach(_velocity, 0d, RunAcceleration * deltaTime);
            _facingYaw = ApproachAngle(_facingYaw, _desiredYaw, 430d * deltaTime);
            state = MotionState.Turn;
            if (now >= _turnUntil && Math.Abs(ShortestAngle(_facingYaw, _desiredYaw)) < 2d)
            {
                _facingYaw = _desiredYaw;
                _turning = false;
                _travelling = true;
            }
        }
        else if (_travelling)
        {
            var distance = _targetLeft - left;
            var remaining = Math.Abs(distance);
            var direction = Math.Sign(distance);

            if (remaining <= 5d)
            {
                left = _targetLeft;
                _velocity = Approach(_velocity, 0d, RunAcceleration * deltaTime);
                if (Math.Abs(_velocity) <= 8d)
                    BeginRest(now);
                state = MotionState.Idle;
            }
            else
            {
                var maxSpeed = _running ? RunSpeed : WalkSpeed;
                var arrival = SmoothStep(Math.Clamp(remaining / ArrivalDistance, 0d, 1d));
                var speedScale = 0.20d + arrival * 0.80d;
                var desiredVelocity = direction * maxSpeed * speedScale;
                var acceleration = _running ? RunAcceleration : WalkAcceleration;
                _velocity = Approach(_velocity, desiredVelocity, acceleration * deltaTime);
                left = Math.Clamp(left + _velocity * deltaTime, minLeft, maxLeft);
                _facingYaw = ApproachAngle(_facingYaw, _desiredYaw, 500d * deltaTime);
                state = Math.Abs(_velocity) >= 118d ? MotionState.Run : MotionState.Walk;
            }
        }
        else
        {
            _velocity = Approach(_velocity, 0d, WalkAcceleration * deltaTime);
            state = MotionState.Idle;
        }

        return new DesktopMotionFrame(left, top, state, _facingYaw, Math.Abs(_velocity), _targetLeft, _running);
    }

    private void ChooseDestination(double left, double minLeft, double maxLeft, double now)
    {
        var span = Math.Max(0d, maxLeft - minLeft);
        if (span < 80d)
        {
            _restUntil = now + 2d;
            return;
        }

        var minimumTravel = Math.Min(Math.Max(180d, span * 0.27d), span * 0.72d);
        var target = left;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            target = minLeft + _random.NextDouble() * span;
            if (Math.Abs(target - left) >= minimumTravel)
                break;
        }

        if (Math.Abs(target - left) < 40d)
            target = left < (minLeft + maxLeft) * 0.5d ? maxLeft : minLeft;

        _targetLeft = Math.Clamp(target, minLeft, maxLeft);
        var direction = Math.Sign(_targetLeft - left);
        _desiredYaw = direction < 0 ? LeftYaw : RightYaw;
        _running = Math.Abs(_targetLeft - left) > 520d && _random.NextDouble() < 0.30d;

        var needsTurn = Math.Abs(ShortestAngle(_facingYaw, _desiredYaw)) > 35d;
        if (needsTurn)
        {
            _turning = true;
            _travelling = false;
            _turnUntil = now + 0.34d;
        }
        else
        {
            _turning = false;
            _travelling = true;
        }
    }

    private void BeginRest(double now)
    {
        _velocity = 0d;
        _travelling = false;
        _turning = false;
        _running = false;
        _restUntil = now + 0.75d + _random.NextDouble() * 1.65d;
    }

    private static (double Min, double Max) HorizontalBounds(Rect workArea, double windowWidth)
    {
        var min = workArea.Left + SideInset;
        var max = Math.Max(min, workArea.Right - windowWidth - SideInset);
        return (min, max);
    }

    private static double Approach(double value, double target, double maximumDelta)
    {
        if (value < target) return Math.Min(target, value + maximumDelta);
        if (value > target) return Math.Max(target, value - maximumDelta);
        return target;
    }

    private static double ApproachAngle(double value, double target, double maximumDelta)
    {
        var delta = ShortestAngle(value, target);
        if (Math.Abs(delta) <= maximumDelta) return target;
        return NormalizeAngle(value + Math.Sign(delta) * maximumDelta);
    }

    private static double ShortestAngle(double from, double to)
    {
        var delta = NormalizeAngle(to) - NormalizeAngle(from);
        if (delta > 180d) delta -= 360d;
        if (delta < -180d) delta += 360d;
        return delta;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360d;
        if (angle < -180d) angle += 360d;
        if (angle > 180d) angle -= 360d;
        return angle;
    }

    private static double SmoothStep(double value) => value * value * (3d - 2d * value);
}
