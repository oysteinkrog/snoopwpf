namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// An <see cref="IIdlingResource"/> that detects WPF layout-pass activity via
/// <see cref="FrameworkElement.LayoutUpdated"/>.
///
/// Opt-in gate for <c>wpf_pump_until_idle</c>: when registered, the AND-gate
/// does not report idle until no <c>LayoutUpdated</c> event has fired for one
/// full <c>DispatcherPriority.ContextIdle</c> tick.  This catches UI patterns
/// where a logical mutation triggers cascading layout invalidations (e.g. a
/// slider scrub that repositions overlay elements) that would otherwise be
/// invisible to the Dispatcher and CompositionRendering resources.
///
/// Implementation notes:
/// <list type="bullet">
///   <item><description>
///     Hooks <see cref="FrameworkElement.LayoutUpdated"/> via
///     <see cref="Application.Current"/>'s <see cref="Application.MainWindow"/>,
///     which receives layout notifications for the full visual tree.
///     Falls back gracefully to idle-always when <c>Application.Current</c> or
///     <c>MainWindow</c> is null (e.g. during unit tests without a running WPF app).
///   </description></item>
///   <item><description>
///     On each <c>LayoutUpdated</c> event: marks busy, cancels any pending
///     re-arm probe, schedules a fresh one at <c>ContextIdle</c> priority.
///   </description></item>
///   <item><description>
///     When the re-arm probe fires at <c>ContextIdle</c> with no intervening
///     <c>LayoutUpdated</c>: marks idle.
///   </description></item>
///   <item><description>
///     Must be instantiated and disposed on the Dispatcher thread (same
///     pattern as <see cref="DispatcherIdlingResource"/> and
///     <see cref="CompositionRenderingResource"/>).
///   </description></item>
/// </list>
/// </summary>
public sealed class LayoutUpdatedIdlingResource : IIdlingResource, IDisposable
{
    private readonly Dispatcher dispatcher;

    private DispatcherOperation? reArmProbe;

    private int isIdle; // 0 = busy, 1 = idle

    private bool disposed;

    private bool subscribed;

    /// <summary>
    /// Initialises a new instance and hooks <c>LayoutUpdated</c> on the application's
    /// main window (if available) on the caller's dispatcher.
    /// </summary>
    public LayoutUpdatedIdlingResource(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        // Start idle; we subscribe on the dispatcher thread.
        this.isIdle = 1;
        this.dispatcher.InvokeAsync(this.Subscribe, DispatcherPriority.Send);
    }

    // ------------------------------------------------------------------ //
    //  IIdlingResource
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => "LayoutUpdated";

    /// <inheritdoc/>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    /// <inheritdoc/>
    public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

    /// <inheritdoc/>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void Subscribe()
    {
        if (this.disposed || this.subscribed)
        {
            return;
        }

        // Hook LayoutUpdated on the main window, which surfaces layout events for
        // the entire visual tree.  If MainWindow is not yet available, fall back to
        // idle-always so we don't block the AND-gate indefinitely.
        var window = Application.Current?.MainWindow;
        if (window is null)
        {
            // No hookable root — treat as permanently idle.
            return;
        }

        this.subscribed = true;
        window.LayoutUpdated += this.OnLayoutUpdated;
    }

    private void Unsubscribe()
    {
        if (!this.subscribed)
        {
            return;
        }

        this.subscribed = false;
        var window = Application.Current?.MainWindow;
        if (window is not null)
        {
            window.LayoutUpdated -= this.OnLayoutUpdated;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        // A layout pass occurred — mark busy.
        this.SetIdle(false);

        // Cancel any pending re-arm probe and schedule a fresh one.
        this.reArmProbe?.Abort();
        this.reArmProbe = this.dispatcher.InvokeAsync(this.OnContextIdle, DispatcherPriority.ContextIdle);
    }

    private void OnContextIdle()
    {
        if (this.disposed)
        {
            return;
        }

        // No LayoutUpdated fired between the last layout pass and this ContextIdle
        // probe — layout has quiesced.
        this.SetIdle(true);
    }

    private void SetIdle(bool idle)
    {
        int newValue = idle ? 1 : 0;
        int previous = Interlocked.Exchange(ref this.isIdle, newValue);
        if (previous != newValue)
        {
            this.IdleChanged?.Invoke(this, new IdleChangedEventArgs
            {
                IsIdle = idle,
                At = DateTimeOffset.UtcNow,
            });
        }
    }

    // ------------------------------------------------------------------ //
    //  IDisposable
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.reArmProbe?.Abort();
        this.reArmProbe = null;
        this.dispatcher.InvokeAsync(this.Unsubscribe, DispatcherPriority.Send);
    }
}
