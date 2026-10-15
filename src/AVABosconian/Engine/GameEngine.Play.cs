namespace AVABosconian.Engine;

public sealed partial class GameEngine
{
    public const double ShipSpeed = 220, ShotSpeed = 620, ShotLife = .55, FireEvery = .14, TurnEvery = .06;
    public const double EnemyShotSpeed = 300;
    public const double StartSeconds = 2.4, DyingSeconds = 2.2, ClearSeconds = 3.0, SafeSeconds = 2.0;
    public const double YellowAfter = 35, RedAfter = 70, SpyEvery = 24, SpyGrace = 4;
    public const int MaxShots = 8;
    public const int PodPoints = 100, BasePoints = 1500, RockPoints = 10, MinePoints = 20, SpyPoints = 800, WingPoints = 50;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 60000;
    public const double MineBlast = 70;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Base> Bases { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Rock> Rocks { get; } = [];
    public Alert Alert { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoundTime { get; private set; }
    /// <summary>For tests: no enemies arrive, no spies, nothing fires, and the alert stays green.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _destroyed;
    private double _spawnIn, _spyIn, _spyGone;
    private bool _spyOut;

    public double Pace => Math.Min(1.7, 1 + 0.1 * (Round - 1));
    public static int BasesIn(int round) => Math.Min(6, 2 + (round + 1) / 2);

    public static int Points(EnemyKind k) => k switch
    {
        EnemyKind.Dart => 50, EnemyKind.Wasp => 60, EnemyKind.Raider => 70, EnemyKind.Spy => SpyPoints, EnemyKind.Leader => 500, _ => WingPoints,
    };

    /// <summary>Shooting a squadron's leader while its wingmen still fly with it is worth more for each of them.</summary>
    public static int LeaderPoints(int wings) => 500 + 250 * wings;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.Start);
        Say($"ROUND {round}", $"{Bases.Count} BASES - BLAST OFF!", StartSeconds);
        Play(Sfx.RoundStart);
    }

    /// <summary>A new patch of space: the bases spread out, asteroids and mines between them, the ship in the middle of nowhere.</summary>
    private void LayOut()
    {
        Bases.Clear();
        Enemies.Clear();
        Shots.Clear();
        Rocks.Clear();
        Effects.Clear();
        RoundTime = 0;
        _destroyed = 0;
        Alert = Alert.Green;
        _spawnIn = 4;
        _spyIn = SpyEvery * .6;
        _spyOut = false;
        var s = Ship;
        (s.X, s.Y) = (Field.World / 2, Field.World / 2);
        s.Dir = 0;
        s.Alive = true;
        s.Safe = SafeSeconds;
        for (int tries = 0; Bases.Count < BasesIn(Round) && tries < 5000; tries++)
        {
            double x = _rng.NextDouble() * Field.World, y = _rng.NextDouble() * Field.World;
            if (Field.Distance(x, y, s.X, s.Y) < 650 || Bases.Any(b => Field.Distance(x, y, b.X, b.Y) < 800)) continue;
            Bases.Add(new Base { X = x, Y = y, Upright = _rng.Next(2) == 0, FireIn = 1 + _rng.NextDouble() * 2, LaunchIn = 4 + _rng.NextDouble() * 3 });
        }
        int rocks = 40 + 4 * Round, mines = 10 + 3 * Round;
        for (int tries = 0; Rocks.Count < rocks + mines && tries < 10000; tries++)
        {
            double x = _rng.NextDouble() * Field.World, y = _rng.NextDouble() * Field.World;
            if (Field.Distance(x, y, s.X, s.Y) < 260 || Bases.Any(b => Field.Distance(x, y, b.X, b.Y) < 200)) continue;
            bool mine = Rocks.Count >= rocks;
            var r = new Rock { X = x, Y = y, Mine = mine, R = mine ? 9 : 14 + _rng.NextDouble() * 12, Spin = (_rng.NextDouble() - .5) * 1.5, Angle = _rng.NextDouble() * 6.3 };
            r.Shape = Enumerable.Range(0, 9).Select(_ => .75 + _rng.NextDouble() * .35).ToArray();
            Rocks.Add(r);
        }
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        foreach (var r in Rocks) r.Angle += r.Spin * dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                Escalate();
                MoveShip(c, dt);
                MoveShots(dt);
                Bases_(dt);
                MoveEnemies(dt);
                Arrivals(dt);
                Collisions();
                if (Phase == Phase.Play && Bases.Count == 0 && _destroyed > 0) Cleared();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveEnemies(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Relaunch();
                }
                break;
            case Phase.Clear:
                if (PhaseTime > ClearSeconds) BeginRound(Round + 1);
                break;
        }
    }

    /// <summary>The alert goes up the longer a round lasts (and straight to red if a spy gets away).</summary>
    private void Escalate()
    {
        if (Calm) return;
        var want = RoundTime > RedAfter ? Alert.Red : RoundTime > YellowAfter ? Alert.Yellow : Alert.Green;
        if (want > Alert) Raise(want);
    }

    private void Raise(Alert a)
    {
        if (a <= Alert) return;
        Alert = a;
        Say(a == Alert.Red ? "CONDITION RED" : "CONDITION YELLOW", null, 1.6);
        Play(Sfx.Alert);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void MoveShip(Controls c, double dt)
    {
        var s = Ship;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        // It turns a step at a time towards the way the stick points, and never stops.
        int want = Dirs.Of(c.Dx, c.Dy);
        s.TurnCool -= dt;
        if (want >= 0 && want != s.Dir && s.TurnCool <= 0)
        {
            int diff = ((want - s.Dir) % 8 + 8) % 8;
            s.Dir = (s.Dir + (diff <= 4 ? 1 : 7)) % 8;
            s.TurnCool = TurnEvery;
        }
        var (vx, vy) = s.Velocity(ShipSpeed);
        s.X = Field.Wrap(s.X + vx * dt);
        s.Y = Field.Wrap(s.Y + vy * dt);
        // Fire: one shot ahead and one astern, each time.
        s.FireCool -= dt;
        if (c.Fire && s.FireCool <= 0 && Shots.Count(x => !x.Enemy) <= MaxShots - 2)
        {
            s.FireCool = FireEvery;
            var (ux, uy) = Dirs.Vec(s.Dir);
            foreach (int k in new[] { 1, -1 })
                Shots.Add(new Shot { X = s.X + ux * 14 * k, Y = s.Y + uy * 14 * k, Vx = vx + ux * ShotSpeed * k, Vy = vy + uy * ShotSpeed * k });
            Play(Sfx.Fire);
        }
    }

    private void MoveShots(double dt)
    {
        foreach (var sh in Shots)
        {
            // In small steps, so a quick shot can't skip over a pod or the core.
            for (int k = 0; k < 4 && !sh.Dead; k++)
            {
                sh.X = Field.Wrap(sh.X + sh.Vx * dt / 4);
                sh.Y = Field.Wrap(sh.Y + sh.Vy * dt / 4);
                if (!sh.Enemy && Phase == Phase.Play) PlayerShotHits(sh);
            }
            sh.Age += dt;
            if (sh.Age > (sh.Enemy ? 2.2 : ShotLife)) sh.Dead = true;
        }
        Shots.RemoveAll(sh => sh.Dead);
    }

    private void PlayerShotHits(Shot sh)
    {
        foreach (var b in Bases)
        {
            if (b.Dead) continue;
            var (dx, dy) = Field.Delta(b.X, b.Y, sh.X, sh.Y);
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > Base.Body + 4) continue;
            for (int i = 0; i < 6; i++)
            {
                if (!b.Pods[i]) continue;
                var (px, py) = b.Pod(i);
                if (Field.Distance(px, py, sh.X, sh.Y) < Base.PodRadius + 2)
                {
                    b.Pods[i] = false;
                    sh.Dead = true;
                    AddScore(PodPoints, px, py);
                    Effects.Add(new Effect { Kind = EffectKind.Burst, X = px, Y = py, Colour = 0xFF72FF8F, Size = .7 });
                    Play(Sfx.PodHit);
                    if (b.Pods.All(p => !p)) Destroy(b);
                    return;
                }
            }
            if (d < Base.CoreRadius)
            {
                // Into the core: only along the opening.
                var (ax, ay) = b.Axis;
                double sp = Math.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy);
                sh.Dead = true;
                if (Math.Abs((sh.Vx * ax + sh.Vy * ay) / sp) > .92) Destroy(b);
                return;
            }
            // Anywhere else in the base, the shot is stopped, unless it's coming down the opening.
            var (axx, ayy) = b.Axis;
            double across = Math.Abs(dx * ayy - dy * axx);
            if (across > Base.CoreRadius) { sh.Dead = true; return; }
        }
        foreach (var e in Enemies)
        {
            if (e.Dead || Field.Distance(e.X, e.Y, sh.X, sh.Y) > (e.Kind == EnemyKind.Spy ? 16 : 13)) continue;
            sh.Dead = true;
            Shoot(e);
            return;
        }
        foreach (var r in Rocks)
        {
            if (r.Dead || Field.Distance(r.X, r.Y, sh.X, sh.Y) > r.R + 2) continue;
            sh.Dead = true;
            Smash(r, byPlayer: true);
            return;
        }
    }

    private void Destroy(Base b)
    {
        b.Dead = true;
        _destroyed++;
        for (int i = 0; i < 6; i++) b.Pods[i] = false;
        AddScore(BasePoints, b.X, b.Y - 30);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = b.X, Y = b.Y, Life = 1.4, Size = 1.6, Colour = 0xFF72FF8F });
        // The blast takes anything close with it.
        foreach (var e in Enemies.Where(e => !e.Dead && Field.Distance(e.X, e.Y, b.X, b.Y) < 90)) Kill(e, points: true);
        Play(Sfx.BaseBoom);
        Bases.Remove(b);
    }

    private void Shoot(Enemy e)
    {
        if (e.Kind == EnemyKind.Leader)
        {
            int wings = Enemies.Count(w => !w.Dead && w.Leader == e);
            e.Dead = true;
            int points = LeaderPoints(wings);
            AddScore(points, e.X, e.Y);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = e.X, Y = e.Y, Colour = 0xFFFFE14A, Size = 1.2 });
            Play(Sfx.Leader);
            // Without a leader the wingmen break off and attack on their own.
            foreach (var w in Enemies.Where(w => w.Leader == e)) { w.Leader = null; w.Kind = EnemyKind.Dart; }
            return;
        }
        if (e.Kind == EnemyKind.Spy) { _spyOut = false; Say("SPY SHIP DOWN", null, 1.2); }
        Kill(e, points: true);
    }

    private void Kill(Enemy e, bool points)
    {
        e.Dead = true;
        if (e.Kind == EnemyKind.Spy) _spyOut = false;
        if (points) AddScore(Points(e.Kind), e.X, e.Y);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = e.X, Y = e.Y, Colour = Colour(e.Kind), Size = .8 });
        Play(Sfx.EnemyHit);
    }

    public static uint Colour(EnemyKind k) => k switch
    {
        EnemyKind.Dart => 0xFFFFE14A, EnemyKind.Wasp => 0xFFFF6AD0, EnemyKind.Raider => 0xFFFF7A3A, EnemyKind.Spy => 0xFF6AE0FF,
        EnemyKind.Leader => 0xFFFFFFFF, _ => 0xFFB0C0FF,
    };

    /// <summary>An asteroid broken up, or a mine set off (taking enemies near it too).</summary>
    private void Smash(Rock r, bool byPlayer)
    {
        r.Dead = true;
        if (r.Mine)
        {
            if (byPlayer) AddScore(MinePoints);
            Effects.Add(new Effect { Kind = EffectKind.Blast, X = r.X, Y = r.Y, Life = .7, Size = .6, Colour = 0xFFFF4A4A });
            foreach (var e in Enemies.Where(e => !e.Dead && Field.Distance(e.X, e.Y, r.X, r.Y) < MineBlast)) Kill(e, points: byPlayer);
            Play(Sfx.MineBoom);
        }
        else
        {
            if (byPlayer) AddScore(RockPoints);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = r.X, Y = r.Y, Colour = 0xFFB0A090, Size = r.R / 20 });
            Play(Sfx.Rock);
        }
        Rocks.Remove(r);
    }

    // ───────────────────────────── the bases ─────────────────────────────

    private void Bases_(double dt)
    {
        var s = Ship;
        foreach (var b in Bases.ToList())
        {
            double d = Field.Distance(b.X, b.Y, s.X, s.Y);
            if (Calm || !s.Alive) continue;
            // The pods fire at the ship when it's near.
            b.FireIn -= dt;
            if (b.FireIn <= 0 && d < 430)
            {
                b.FireIn = (1.6 + _rng.NextDouble()) / Pace;
                int best = -1;
                double near = double.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    if (!b.Pods[i]) continue;
                    var (px, py) = b.Pod(i);
                    double pd = Field.Distance(px, py, s.X, s.Y);
                    if (pd < near) { near = pd; best = i; }
                }
                if (best >= 0) { var (px, py) = b.Pod(best); FireAt(px, py); }
            }
            // And now and then the base sends out a fighter.
            b.LaunchIn -= dt;
            if (b.LaunchIn <= 0 && d < 750)
            {
                b.LaunchIn = (6 + _rng.NextDouble() * 3) / Pace;
                var (dx, dy) = Field.Delta(b.X, b.Y, s.X, s.Y);
                Enemies.Add(new Enemy { Kind = EnemyKind.Dart, X = b.X, Y = b.Y, Angle = Math.Atan2(dx, -dy), Speed = Speed(EnemyKind.Dart), FireIn = 2 });
            }
        }
    }

    private void FireAt(double x, double y)
    {
        var s = Ship;
        var (dx, dy) = Field.Delta(x, y, s.X, s.Y);
        double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), t = d / EnemyShotSpeed;
        // Leading the ship a little.
        var (vx, vy) = s.Velocity(ShipSpeed);
        dx += vx * t * .6; dy += vy * t * .6;
        d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        Shots.Add(new Shot { X = x, Y = y, Vx = dx / d * EnemyShotSpeed, Vy = dy / d * EnemyShotSpeed, Enemy = true });
        Play(Sfx.EnemyFire);
    }

    // ───────────────────────────── the enemies ─────────────────────────────

    private double Speed(EnemyKind k) => (k switch
    {
        EnemyKind.Dart => 240, EnemyKind.Wasp => 190, EnemyKind.Raider => 205, EnemyKind.Spy => 250, EnemyKind.Leader or EnemyKind.Wing => 180, _ => 200,
    }) * Math.Min(1.3, Pace);

    private static double TurnRate(EnemyKind k) => k switch { EnemyKind.Dart => 2.0, EnemyKind.Wasp => 3.4, EnemyKind.Raider => 1.8, EnemyKind.Spy => 0, _ => 1.5 };

    /// <summary>New arrivals from beyond the view: fighters as the alert allows, spies now and then, and squadrons once it's red.</summary>
    private void Arrivals(double dt)
    {
        if (Calm || !Ship.Alive) return;
        int max = Alert switch { Alert.Green => 3, Alert.Yellow => 6, _ => 10 };
        double every = Alert switch { Alert.Green => 4.0, Alert.Yellow => 2.4, _ => 1.4 } / Pace;
        _spawnIn -= dt;
        int fighters = Enemies.Count(e => !e.Dead && e.Kind is EnemyKind.Dart or EnemyKind.Wasp or EnemyKind.Raider);
        if (_spawnIn <= 0)
        {
            _spawnIn = every * (.7 + .6 * _rng.NextDouble());
            if (fighters < max)
            {
                double roll = _rng.NextDouble();
                var kind = roll < .45 ? EnemyKind.Dart : roll < .75 ? EnemyKind.Wasp : EnemyKind.Raider;
                var (x, y, a) = Offscreen();
                Enemies.Add(new Enemy { Kind = kind, X = x, Y = y, Angle = a, Speed = Speed(kind), FireIn = 1.5 + _rng.NextDouble() * 2 });
            }
            // A squadron, when the alert is red.
            if (Alert == Alert.Red && _rng.NextDouble() < .25 && !Enemies.Any(e => !e.Dead && e.Kind == EnemyKind.Leader)) Squadron();
        }
        // The spy ship: across the view, and if it gets clear away, trouble.
        _spyIn -= dt;
        if (_spyIn <= 0 && !_spyOut)
        {
            _spyIn = SpyEvery * (.8 + .4 * _rng.NextDouble());
            var (x, y, a) = Offscreen();
            // It flies across the view rather than at the ship.
            a += (_rng.NextDouble() - .5) * .8;
            Enemies.Add(new Enemy { Kind = EnemyKind.Spy, X = x, Y = y, Angle = a, Speed = Speed(EnemyKind.Spy) });
            _spyOut = true;
            _spyGone = 0;
            Play(Sfx.Spy);
        }
        if (_spyOut && Enemies.FirstOrDefault(e => !e.Dead && e.Kind == EnemyKind.Spy) is { } spy)
        {
            var (dx, dy) = Field.Delta(Ship.X, Ship.Y, spy.X, spy.Y);
            bool outOfView = Math.Abs(dx) > Field.ViewW / 2 + 40 || Math.Abs(dy) > Field.ViewH / 2 + 40;
            _spyGone = outOfView && spy.T > 2 ? _spyGone + dt : 0;
            if (_spyGone > SpyGrace)
            {
                spy.Dead = true;
                _spyOut = false;
                Raise(Alert.Red);
                Say("SPY SHIP ESCAPED", "CONDITION RED", 2);
                Play(Sfx.SpyAway);
                Squadron();
            }
        }
    }

    /// <summary>A place just beyond the view, and the heading from there towards the ship.</summary>
    private (double X, double Y, double Angle) Offscreen()
    {
        var s = Ship;
        double side = _rng.Next(4), along = _rng.NextDouble() - .5;
        double dx = side switch { 0 => -Field.ViewW / 2 - 60, 1 => Field.ViewW / 2 + 60, _ => along * Field.ViewW },
               dy = side switch { 2 => -Field.ViewH / 2 - 60, 3 => Field.ViewH / 2 + 60, _ => along * Field.ViewH };
        return (Field.Wrap(s.X + dx), Field.Wrap(s.Y + dy), Math.Atan2(-dx, dy));
    }

    private void Squadron()
    {
        var (x, y, a) = Offscreen();
        var lead = new Enemy { Kind = EnemyKind.Leader, X = x, Y = y, Angle = a, Speed = Speed(EnemyKind.Leader), FireIn = 2 };
        Enemies.Add(lead);
        foreach (double slot in new[] { -2.0, -1, 1, 2 })
            Enemies.Add(new Enemy { Kind = EnemyKind.Wing, X = x, Y = y, Angle = a, Speed = lead.Speed, Leader = lead, Slot = slot });
        Say("ALERT - SQUADRON", null, 1.6);
        Play(Sfx.Formation);
    }

    private void MoveEnemies(double dt)
    {
        var s = Ship;
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            e.T += dt;
            if (e.Kind == EnemyKind.Wing && e.Leader is { Dead: false } lead)
            {
                // In a V behind the leader.
                double back = Math.Abs(e.Slot) * 22, side = e.Slot * 20;
                double fx = Math.Sin(lead.Angle), fy = -Math.Cos(lead.Angle);
                e.X = Field.Wrap(lead.X - fx * back - fy * side);
                e.Y = Field.Wrap(lead.Y - fy * back + fx * side);
                e.Angle = lead.Angle;
                continue;
            }
            if (e.Kind != EnemyKind.Spy && s.Alive)
            {
                var (dx, dy) = Field.Delta(e.X, e.Y, s.X, s.Y);
                double want = Math.Atan2(dx, -dy), err = Math.Atan2(Math.Sin(want - e.Angle), Math.Cos(want - e.Angle));
                e.Angle += Math.Clamp(err, -TurnRate(e.Kind) * dt, TurnRate(e.Kind) * dt);
                if (e.Kind == EnemyKind.Raider && !Calm && Phase == Phase.Play)
                {
                    e.FireIn -= dt;
                    if (e.FireIn <= 0 && dx * dx + dy * dy < 380 * 380) { e.FireIn = (2 + _rng.NextDouble()) / Pace; FireAt(e.X, e.Y); }
                }
            }
            e.X = Field.Wrap(e.X + Math.Sin(e.Angle) * e.Speed * dt);
            e.Y = Field.Wrap(e.Y - Math.Cos(e.Angle) * e.Speed * dt);
        }
        // Fighters that fly into asteroids or mines are lost too.
        foreach (var e in Enemies.Where(e => !e.Dead && e.Kind != EnemyKind.Spy))
            foreach (var r in Rocks)
                if (!r.Dead && Field.Distance(e.X, e.Y, r.X, r.Y) < r.R + 8)
                {
                    Kill(e, points: false);
                    if (r.Mine) Smash(r, byPlayer: false);
                    break;
                }
        Rocks.RemoveAll(r => r.Dead);
        Enemies.RemoveAll(e => e.Dead);
    }

    // ───────────────────────────── meetings, and the round's end ─────────────────────────────

    private void Collisions()
    {
        var s = Ship;
        if (!s.Alive || s.Safe > 0) return;
        foreach (var sh in Shots)
            if (sh.Enemy && !sh.Dead && Field.Distance(sh.X, sh.Y, s.X, s.Y) < 10) { sh.Dead = true; Die("SHOT"); return; }
        foreach (var e in Enemies)
            if (!e.Dead && Field.Distance(e.X, e.Y, s.X, s.Y) < 18) { Kill(e, points: true); Die("RAMMED"); return; }
        foreach (var r in Rocks)
            if (!r.Dead && Field.Distance(r.X, r.Y, s.X, s.Y) < r.R + 9)
            {
                bool mine = r.Mine;
                Smash(r, byPlayer: false);
                Die(mine ? "MINE" : "ASTEROID");
                return;
            }
        foreach (var b in Bases)
        {
            if (Field.Distance(b.X, b.Y, s.X, s.Y) < Base.Body - 6) { Die("BASE"); return; }
            for (int i = 0; i < 6; i++)
            {
                if (!b.Pods[i]) continue;
                var (px, py) = b.Pod(i);
                if (Field.Distance(px, py, s.X, s.Y) < Base.PodRadius + 9) { Die("BASE"); return; }
            }
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Ship.Alive = false;
        Lives--;
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Ship.X, Y = Ship.Y, Life = 1.6, Size = 1.1, Colour = 0xFFFFFFFF });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The next ship, where the last one was, with the space round it cleared.</summary>
    private void Relaunch()
    {
        var s = Ship;
        Shots.Clear();
        Enemies.RemoveAll(e => Field.Distance(e.X, e.Y, s.X, s.Y) < 500);
        Rocks.RemoveAll(r => Field.Distance(r.X, r.Y, s.X, s.Y) < 120);
        // Not inside a base.
        while (Bases.Any(b => Field.Distance(b.X, b.Y, s.X, s.Y) < 160)) s.Y = Field.Wrap(s.Y + 60);
        s.Alive = true;
        s.Safe = SafeSeconds;
        _spyOut = false;
        Enemies.RemoveAll(e => e.Kind == EnemyKind.Spy);
        SetPhase(Phase.Start);
        Say("READY", null, StartSeconds);
    }

    private void Cleared()
    {
        SetPhase(Phase.Clear);
        Music(false);
        Shots.Clear();
        Say("ROUND CLEAR", null, ClearSeconds);
        Play(Sfx.Clear);
    }

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = points >= 500 ? 0xFFFFE14A : 0xFFFFFFFF, Life = 1.0 });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round.</summary>
    internal void Go(int round = 1)
    {
        StartGame();
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>Empty space: no bases, rocks or enemies.</summary>
    internal void Clear()
    {
        Bases.Clear();
        Rocks.Clear();
        Enemies.Clear();
        Shots.Clear();
    }

    internal void PutShip(double x, double y, int dir)
    {
        var s = Ship;
        (s.X, s.Y, s.Dir) = (x, y, dir);
        s.Alive = true;
        s.Safe = 0;
    }

    internal Base PutBase(double x, double y, bool upright)
    {
        var b = new Base { X = x, Y = y, Upright = upright, FireIn = 99, LaunchIn = 99 };
        Bases.Add(b);
        return b;
    }

    internal Enemy PutEnemy(EnemyKind kind, double x, double y, double angle)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, Angle = angle, Speed = Speed(kind), FireIn = 99 };
        Enemies.Add(e);
        return e;
    }

    internal Rock PutRock(double x, double y, bool mine = false)
    {
        var r = new Rock { X = x, Y = y, Mine = mine, R = mine ? 9 : 18, Shape = Enumerable.Repeat(1.0, 9).ToArray() };
        Rocks.Add(r);
        return r;
    }

    internal Shot PutShot(double x, double y, double vx, double vy)
    {
        var sh = new Shot { X = x, Y = y, Vx = vx, Vy = vy };
        Shots.Add(sh);
        return sh;
    }

    internal Enemy ReleaseSpy(double x, double y, double angle)
    {
        var e = PutEnemy(EnemyKind.Spy, x, y, angle);
        _spyOut = true;
        _spyGone = 0;
        _spyIn = 999;
        return e;
    }

    internal void SquadronForTest() => Squadron();
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetRoundTime(double t) => RoundTime = t;
}
