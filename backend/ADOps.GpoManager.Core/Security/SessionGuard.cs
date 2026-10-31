namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Garde de session : verrouillage automatique d'une console d'administration
/// inactive (principe du moindre privilège dans le temps). Le garde est
/// indépendant de l'interface : la vue alimente <see cref="ReportActivity"/>
/// sur chaque interaction et interroge <see cref="ShouldLock"/> périodiquement.
/// </summary>
public sealed class SessionGuard
{
    private readonly object _gate = new();
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private bool _locked;

    public SessionGuard(TimeSpan idleTimeout)
    {
        IdleTimeout = idleTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : idleTimeout;
    }

    /// <summary>Durée d'inactivité avant verrouillage automatique.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>Vrai après un verrouillage (jusqu'à ré-authentification).</summary>
    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                return _locked;
            }
        }
    }

    /// <summary>Durée d'inactivité courante.</summary>
    public TimeSpan IdleFor
    {
        get
        {
            lock (_gate)
            {
                return DateTimeOffset.UtcNow - _lastActivity;
            }
        }
    }

    /// <summary>Temps restant avant verrouillage (0 si déjà verrouillé).</summary>
    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                var remaining = IdleTimeout - IdleFor;
                return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
        }
    }

    /// <summary>Événement déclenché quand la session doit être verrouillée.</summary>
    public event EventHandler? LockRequested;

    /// <summary>Enregistre une interaction utilisateur (souris, clavier, commande).</summary>
    public void ReportActivity()
    {
        lock (_gate)
        {
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// À appeler périodiquement (ex. : toutes les secondes) : déclenche
    /// <see cref="LockRequested"/> si l'inactivité dépasse le délai.
    /// </summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_locked)
            {
                return;
            }

            if (IdleFor >= IdleTimeout)
            {
                _locked = true;
            }
        }

        if (IsLocked)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Verrouillage manuel (bouton « Verrouiller la session »).</summary>
    public void Lock()
    {
        lock (_gate)
        {
            _locked = true;
        }

        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Réarme la garde après une ré-authentification réussie.</summary>
    public void Unlock()
    {
        lock (_gate)
        {
            _locked = false;
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }
}
