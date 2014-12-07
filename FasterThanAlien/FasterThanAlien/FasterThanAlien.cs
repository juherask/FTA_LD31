using System;
using System.Collections.Generic;
using Jypeli;
using Jypeli.Assets;
using Jypeli.Controls;
using Jypeli.Effects;
using Jypeli.Widgets;

public class FasterThanAlien : PhysicsGame
{
    /* Game constants (movement, graphics) */
    const int QUEEN_MANUAL_MOVE_SPEED = 1000;
    const int ALIEN_WANDER_SPEED = 60;
    const int ALIEN_ATTACK_SPEED = 100;
    const int CREW_WANDER_SPEED = 30;
    const int CREW_CATCH_SPEED = 50;
    const int CREW_FLEE_SPEED = 60;
    const double GRAB_WEAPON_TIME = 3.0; // in secs
    const double ALIEN_GROW_INTERVAL = 5.00; // in secs
    const int TILE_SIZE = 32;
    const int NEST_SIZE = 32 * 4;
    const int PREMATURE_OPTIMIZATION_UPDATE_LOF_EVERY_N_TH_TICK = 20;
    const int SPAWN_INCUBATION_TIME = 10; // in secs

    /* Resources */
    Animation spawnIdleAnimation;
    Animation alienIdleAnimation;
    Animation queenIdleAnimation;
    Animation crewIdleAnimation;
    Animation deadCrewAnimation;
    Image floorImage, hotFloorImage, doorImage, grilleImage, bentGrilleImage, ductImage, hullImage, ceilImage;
    SoundEffect alienDiesSound, humanDiesSound, grilleSmashSound;
    
    /* Preconditions for nesting */
    GameObject hugeIndicator;
    GameObject hostIndicator;
    GameObject heatIndicator;
    bool isHuge = false;
    public bool IsHuge
    {
        get
        {
            return isHuge;
        }
        set
        {
            if (isHuge!=value)
                hugeIndicator.Animation.Step(); // Toggle 
            isHuge = value;
        }
    }
    bool hasHost = false;
    public bool HasHost
    {
        get
        {
            return hasHost;
        }
        set
        {
            if (hasHost != value)
                hostIndicator.Animation.Step(); // Toggle 
            hasHost = value;
        }
    }
    bool hasHeat = false;
    public bool HasHeat
    {
        get
        {
            return hasHeat;
        }
        set
        {
            if (hasHeat != value)
                heatIndicator.Animation.Step(); // Toggle 
            hasHeat = value;
        }
    }

    /* Rest of the game state */
    PhysicsObject queen;
    PhysicsObject controlledAlien;
    bool isNesting = false;
    List<PhysicsObject> crewMembers = new List<PhysicsObject>();
    List<PhysicsObject> aliens = new List<PhysicsObject>();
    int updateTickCounter = 0;

    bool hasShownNestingTip = false;
    bool hasShownControlTip = false;

    GameObject introScreen;
    GameObject introBlink;
    Timer introBlinkTimer;
    bool gameStarted = false;

    #region initialization
    public override void Begin()
    {
        //SetWindowSize(1366, 768);
        ShowIntro();
    }

    void ShowIntro()
    {
        Level.Background.Color = Color.Black;

        Image introImage = LoadImage("intro");
        introScreen = new GameObject(introImage.Width, introImage.Height);
        introScreen.Image = introImage;
        Add(introScreen);

        Image startImage = LoadImage("caught");
        introBlink = new GameObject(startImage.Width, startImage.Height);
        introBlink.Image = startImage;
        introBlink.Y = -125;
        Add(introBlink,1);

        introBlinkTimer = new Timer();
        introBlinkTimer.Interval = 1.0;
        introBlinkTimer.Timeout += () => introBlink.IsVisible = !introBlink.IsVisible;
        introBlinkTimer.Start();

        Keyboard.Listen(Key.Escape, ButtonState.Pressed, ConfirmExit, "Exit game");
        Keyboard.Listen(Key.Space, ButtonState.Released, BeginGame, "Start the game");
    }

    void BeginGame() {
        if (gameStarted) return;

        introBlinkTimer.Stop();
        introScreen.Destroy();
        introBlink.Destroy();

        SmoothTextures = false;

        LoadAnimationsAndImages();
        
        ColorTileMap level = new ColorTileMap(LoadImage("nostromo"));

        // Ship corridors, floorts etc.
        level.SetTileMethod(Color.FromPaintDotNet(1, 1), CreateWall);
        level.SetTileMethod(Color.FromHexCode("606060"), CreateCeiling);
        level.SetTileMethod(Color.White, CreateFloor);
        level.SetTileMethod(Color.Yellow, CreateHotFloor);
        level.SetTileMethod(Color.FromPaintDotNet(0, 9), CreateDuct);
        level.SetTileMethod(Color.FromPaintDotNet(0, 10), CreateGrille);
        level.SetTileMethod(Color.FromPaintDotNet(0, 1), CreateTechnicalSpace);
        level.SetTileMethod(Color.FromPaintDotNet(0, 6), CreateDoor, Direction.Left);
        level.SetTileMethod(Color.FromPaintDotNet(1, 6), CreateDoor, Direction.Up);

        // Creatures
        level.SetTileMethod(Color.FromPaintDotNet(0, 2), CreateCrewMember);
        level.SetTileMethod(Color.Black, CreateSpawn);

        // Others
        level.SetTileMethod(Color.FromPaintDotNet(0, 4), CreateAfterburner);

        level.Execute(TILE_SIZE, TILE_SIZE);

        Level.Background.CreateStars();

        SetControls();

        CreateIndicators();

        gameStarted = true;
    }



    static Color destaturate(Color c)
    {
        int avg = (c.BlueComponent + c.RedComponent + c.GreenComponent) / 3;
        return new Color(avg, avg, Math.Min(255,avg+30), c.AlphaComponent);
    }
    void CreateIndicators()
    {
        hugeIndicator = new GameObject(50, 50);
        Image notActiveHugeImage = LoadImage("huge");
        notActiveHugeImage.ApplyPixelOperation(destaturate);
        hugeIndicator.Animation = new Animation(
            notActiveHugeImage,
            LoadImage("huge"));
        hugeIndicator.Position = new Vector(Screen.LeftSafe + 30, Screen.TopSafe - 30);
        Add(hugeIndicator);

        hostIndicator = new GameObject(50, 50);
        Image notActiveHostImage = LoadImage("host");
        notActiveHostImage.ApplyPixelOperation(destaturate);
        hostIndicator.Animation = new Animation(
            notActiveHostImage,
            LoadImage("host"));
        hostIndicator.Position = new Vector(Screen.LeftSafe + 90, Screen.TopSafe - 30);
        Add(hostIndicator);

        heatIndicator = new GameObject(50, 50);
        Image notActiveHeatImage = LoadImage("heat");
        notActiveHeatImage.ApplyPixelOperation(destaturate);
        heatIndicator.Animation = new Animation(
            notActiveHeatImage,
            LoadImage("heat"));
        heatIndicator.Position = new Vector(Screen.LeftSafe + 150, Screen.TopSafe - 30);
        Add(heatIndicator);
    }

    void SetControls()
    {
        Keyboard.Listen(Key.Left, ButtonState.Down, MoveAlien, "Move the alien with arrow keys", new Vector(-QUEEN_MANUAL_MOVE_SPEED, 0) );
        Keyboard.Listen(Key.Right, ButtonState.Down, MoveAlien, "Move the alien with arrow keys", new Vector(QUEEN_MANUAL_MOVE_SPEED, 0));
        Keyboard.Listen(Key.Down, ButtonState.Down, MoveAlien, "Move the alien with arrow keys", new Vector(0, -QUEEN_MANUAL_MOVE_SPEED));
        Keyboard.Listen(Key.Up, ButtonState.Down, MoveAlien, "Move the alien with arrow keys", new Vector(0, QUEEN_MANUAL_MOVE_SPEED));
        Keyboard.Listen(Key.N, ButtonState.Released, BuildNest, "Build the nest");
        Keyboard.Listen(Key.D1, ButtonState.Released, ControlSpawn, "Take control of a spawn", 1);
        Keyboard.Listen(Key.D2, ButtonState.Released, ControlSpawn, "Take control of a spawn", 2);
        Keyboard.Listen(Key.D3, ButtonState.Released, ControlSpawn, "Take control of a spawn", 3);
        Keyboard.Listen(Key.D4, ButtonState.Released, ControlSpawn, "Take control of a spawn", 4);
        Keyboard.Listen(Key.D5, ButtonState.Released, ControlSpawn, "Take control of a spawn", 5);
        Keyboard.Listen(Key.D6, ButtonState.Released, ControlSpawn, "Take control of a spawn", 6);
        Keyboard.Listen(Key.D7, ButtonState.Released, ControlSpawn, "Take control of a spawn", 7);
        Keyboard.Listen(Key.D8, ButtonState.Released, ControlSpawn, "Take control of a spawn", 8);
        Keyboard.Listen(Key.D9, ButtonState.Released, ControlSpawn, "Take control of a spawn", 9);

    }

    void LoadAnimationsAndImages()
    {
        Image spawnGridImage = LoadImage("spawn");
        spawnIdleAnimation = new Animation(new Image[] { spawnGridImage.Area(0, 0, 15, 15), spawnGridImage.Area(16, 0, 31, 15) });
        spawnIdleAnimation.FPS = 3;
        spawnIdleAnimation.IsPlaying = true;

        Image alienGridImage = LoadImage("alien");
        alienIdleAnimation = new Animation(new Image[] { alienGridImage.Area(0, 0, 15, 15), alienGridImage.Area(16, 0, 31, 15) });
        alienIdleAnimation.FPS = 3;
        alienIdleAnimation.IsPlaying = true;

        Image queenGridImage = LoadImage("queen");
        queenIdleAnimation = new Animation(new Image[] {
            queenGridImage.Area(0, 0, 23, 23),
            queenGridImage.Area(24, 0, 47, 23),
            queenGridImage.Area(48, 0, 71, 23)});
        queenIdleAnimation.FPS = 4;
        queenIdleAnimation.IsPlaying = true;

        Image crewGridImage = LoadImage("crew");
        crewIdleAnimation = new Animation(new Image[] { crewGridImage.Area(0, 0, 15, 15), crewGridImage.Area(16, 0, 31, 15) });
        crewIdleAnimation.FPS = 2;
        crewIdleAnimation.IsPlaying = true;
        deadCrewAnimation = new Animation(new Image[] { 
            crewGridImage.Area(16*0, 16*1, 16*0+15, 16*1+15),
            crewGridImage.Area(16*1, 16*1, 16*1+15, 16*1+15) });
        deadCrewAnimation.FPS = 2;
        

        doorImage = LoadImage("door");
        floorImage = LoadImage("floor");
        hotFloorImage = floorImage.Clone();
        hotFloorImage.ApplyPixelOperation(c=>{
            c.RedComponent = Math.Min((byte)255,c.RedComponent+=100);
            c.GreenComponent = Math.Min((byte)255,c.GreenComponent+=30);
            return Color.Darker(c, 30);});
        grilleImage = LoadImage("grille");
        bentGrilleImage = LoadImage("bent_grille");
        ductImage = LoadImage("duct");
        hullImage = LoadImage("hull");
        ceilImage = LoadImage("ceil");

        alienDiesSound = LoadSoundEffect("alien_dead");
        humanDiesSound = LoadSoundEffect("human_dead");
        grilleSmashSound = LoadSoundEffect("grille_smash");
    }

    void CreateWall(Vector pos, double w, double h)
    {
        PhysicsObject wall = PhysicsObject.CreateStaticObject(w, h);
        wall.Position = pos;
        wall.Image = hullImage;
        wall.Tag = "wall";
        Add(wall,2);    
    }
    void CreateCeiling(Vector pos, double w, double h)
    {
        PhysicsObject wall = PhysicsObject.CreateStaticObject(w, h);
        wall.Position = pos;
        wall.Image = ceilImage;
        wall.Tag = "wall";
        Add(wall,2);
    }
    void CreateFloor(Vector pos, double w, double h)
    {
        GameObject floor = new GameObject(w, h);
        floor.Position = pos;
        floor.Image = floorImage;
        floor.Tag = "floor";
        Add(floor,-3);    
    }
    void CreateHotFloor(Vector pos, double w, double h)
    {
        GameObject floor = new GameObject(w, h);
        floor.Position = pos;
        floor.Image = hotFloorImage;
        floor.Tag = "floor";
        Add(floor, -3);
    }
    void CreateDuct(Vector pos, double w, double h)
    {
        GameObject duct = new GameObject(w, h);
        duct.Position = pos;
        duct.Image = ductImage;
        Add(duct, -3);
    }
    void CreateGrille(Vector pos, double w, double h)
    {
        PhysicsObject grille = PhysicsObject.CreateStaticObject(w, h);
        grille.Position = pos;
        grille.Image = grilleImage;
        grille.Tag = "wall"; /*hu-man brains need this*/
        Add(grille,-2);

        // Same group with alien spawn
        grille.CollisionIgnoreGroup = 1;
        AddCollisionHandlerByTag<PhysicsObject,PhysicsObject>(grille, "alien", GrilleGetsTorn);
    }
    void CreateTechnicalSpace(Vector pos, double w, double h)
    {
        GameObject floor = new GameObject(w, h);
        floor.Position = pos;
        floor.Color = Color.Darker(Color.SlateGray, 100);
        Add(floor,-3);    
    }
    void CreateDoor(Vector pos, double w, double h, Direction dir)
    {
        PhysicsObject door = PhysicsObject.CreateStaticObject(w, h);
        door.Position = pos;
        door.Image = doorImage;
        door.IgnoresCollisionResponse = true;
        AddCollisionHandler(door, OpenDoor);
        if (dir == Direction.Up) door.Angle = Angle.FromDegrees(180+90);
        Add(door,-2);
    }
    void CreateSpawn(Vector pos, double w, double h)
    {
        CreateFloor(pos, w, h);
        PhysicsObject body = CreateCrewBody(pos, h, w);
        body.IgnoresCollisionResponse = true;

        PhysicsObject alien = SpawnSpawn(w, h);
        alien.Position = pos;
        queen = alien;
        controlledAlien = queen;
    }

    private PhysicsObject SpawnSpawn(double w, double h)
    {
        // The alien object that interacts
        PhysicsObject alien = new PhysicsObject(w / 2, h / 2); // spawn is half size
        
        alien.Color = Color.Transparent;
        alien.CanRotate = false;
        alien.Restitution = 0.0;
        alien.LinearDamping = 0.75;

        // Compose some functionlality
        GameObject alienSkin = new GameObject(w, h);
        alienSkin.Animation = spawnIdleAnimation;
        alienSkin.Tag = "skin";
        alien.Add(alienSkin);
        alien.Tag = "spawn";

        // Can pass grilles (same CIG)
        alien.CollisionIgnoreGroup = 1;

        Add(alien, 1);
        aliens.Add(alien);

        Timer growTimer = new Timer();
        growTimer.Interval = ALIEN_GROW_INTERVAL;
        growTimer.Timeout += () => AlienGrows(growTimer, alien);
        growTimer.Start();

        AddCollisionHandlerByTag<PhysicsObject, PhysicsObject>(alien, "crew", CrewGetsEaten);
        return alien;
    }

    void CreateCrewMember(Vector pos, double w, double h)
    {
        CreateFloor(pos, w, h);

        // The crew object that interacts
        PhysicsObject crew = new PhysicsObject(w/2, h-2);
        crew.Position = pos;
        crew.Color = Color.Transparent;
        crew.CanRotate = false;

        // Compose some functionlality
        GameObject crewSkin = new GameObject(w, h);
        crewSkin.Animation = crewIdleAnimation;
        crewSkin.Tag = "skin";
        crew.Add(crewSkin);
        MakeCritterWander(crew, CREW_WANDER_SPEED);

        crew.Tag = "crew";

        Add(crew,0);
        crewMembers.Add(crew);
    }

    void CreateAfterburner(Vector pos, double w, double h)
    {
        Flame flame = new Flame(LoadImage("flames"));
        flame.Angle = Angle.FromDegrees(0);
        flame.Position = pos;
        Add(flame);
    }
    #endregion

    #region state
    protected override void Update(Time time)
    {
        base.Update(time);
        if (!gameStarted) return;
        updateTickCounter++;
        if (updateTickCounter > 1000)
            updateTickCounter = 10;

        // TODO: This is potentially very expensive.
        //  perhaps it should be checked every Nth update?
        //  -> done, just in case. (it also induces nice
        //  "detecting" the alien random delay to)
        if (updateTickCounter % PREMATURE_OPTIMIZATION_UPDATE_LOF_EVERY_N_TH_TICK == 0)
        {
            DoAliensAndCrewDetection();

            // Leave a blood trail
            DoDripBloodTrail();

            // Produce new spawns
            CheckForBodiesInTheNest();

            if (HasHeat && HasHost && IsHuge && !hasShownNestingTip)
            {
                hasShownNestingTip = true;
                Label nestingTip = new Label("You have a host, heat and you are of the right age.\nQuick! Press 'N' to build a nest.");
                nestingTip.TextColor = Color.White;
                GameObject tipBackground = new GameObject(nestingTip.Width+50, nestingTip.Height+50);
                tipBackground.Color = Color.Black;
                Add(tipBackground, 3);
                Add(nestingTip,3);
                Timer.SingleShot(5.0, () => { Remove(nestingTip); Remove(tipBackground); });
            }
        }

        CheckQueenDetectsHeat();
        CheckQueenHasAChees__ImeanBody();
    }

    private void DoDripBloodTrail()
    {
        foreach (var body in GetObjectsWithTag("host"))
        {
            if (body is PhysicsObject && ((PhysicsObject)body).Velocity.Magnitude > 10)
            {
                Color bloodColor = Color.Darker(Color.BloodRed, 10);
                double size = RandomGen.NextDouble(4.0, 7.0);
                GameObject dropOfBlood = new GameObject(TILE_SIZE / size, TILE_SIZE / size);
                dropOfBlood.Shape = Shape.Circle;
                dropOfBlood.Angle = Angle.RightAngle;
                dropOfBlood.Color = bloodColor;
                dropOfBlood.Position = body.Position;
                dropOfBlood.Tag = "blood";
                Add(dropOfBlood, -2);
            }
        }
    }

    private void CheckQueenDetectsHeat()
    {
        GameObject queenStandsOn = GetObjectAt(queen.Position, "floor");
        if (queenStandsOn != null &&
            queenStandsOn.Image == hotFloorImage)
        {
            if (!HasHeat)
                HasHeat = true;
        }
        else if (HasHeat)
        {
            HasHeat = false;
        }
    }
    private void CheckQueenHasAChees__ImeanBody()
    {
        GameObject queenIsNextToABody = GetObjectAt(queen.Position, "host", TILE_SIZE);
        if (queenIsNextToABody != null)
        {
            if (!HasHost)
                HasHost = true;
        }
        else if (HasHost)
        {
            HasHost = false;
        }
    }

    private void DoAliensAndCrewDetection()
    {
        /*Aliens sees crew*/
        foreach (var alien in aliens)
        {
            // Not player controlled, not mindless spawn
            if (alien!=controlledAlien && alien.Brain != null && (string)alien.Tag != "spawn")
            {
                PhysicsObject target = null;
                foreach (var crew in crewMembers)
                {
                    if (alien.SeesObject(crew, go => go.Image == doorImage || (string)go.Tag == "wall"))
                    {
                        target = crew;
                    }
                }

                if (target == null && !(alien.Brain is LabyrinthWandererBrain))
                {
                    MakeCritterWander(alien, ALIEN_WANDER_SPEED);
                }
                else if (target != null && (alien.Brain is LabyrinthWandererBrain))
                {
                    // Attack!
                    FollowerBrain brains = new FollowerBrain(target);
                    brains.Speed = ALIEN_ATTACK_SPEED;
                    alien.Brain = brains;
                }
            }
        }

        /*Crew sees alien*/
        foreach (var crew in crewMembers)
        {
            PhysicsObject seesSpawn = null;
            PhysicsObject seesAlien = null;
            foreach (var alien in aliens)
            {
                if (crew.SeesObject(alien, go => go.Image == doorImage || (string)go.Tag == "wall"))
                {
                    if ((string)alien.Tag == "spawn")
                        seesSpawn = alien;
                    else
                        seesAlien = alien;
                }
            }

            bool isFleeing = crew.Brain is FollowerBrain && ((FollowerBrain)crew.Brain).Speed < 0;

            if (seesAlien != null && (crew.Brain is LabyrinthWandererBrain || !isFleeing))
            {
                //MessageDisplay.Add("set flee brain");
                FollowerBrain brains = new FollowerBrain(seesAlien);
                brains.Speed = -CREW_CATCH_SPEED;
                crew.Brain = brains;
            }
            else if (seesSpawn != null && crew.Brain is LabyrinthWandererBrain)
            {
                //MessageDisplay.Add("set catch brain");
                FollowerBrain brains = new FollowerBrain(seesSpawn);
                brains.Speed = CREW_CATCH_SPEED;
                crew.Brain = brains;
            }
            else if (seesAlien == null && seesSpawn == null && crew.Brain is FollowerBrain)
            {
                //MessageDisplay.Add("set wander brain");
                MakeCritterWander(crew, CREW_WANDER_SPEED);
            }

            bool hasWeapon = false;
            foreach (var potentialWeapon in crew.Objects)
            {
                if (potentialWeapon is Weapon)
                    hasWeapon = true;
            }
            if (!hasWeapon)
            {
                foreach (var blood in GetObjectsWithTag("blood"))
                {
                    if (crew.SeesObject(blood, go => go.Image == doorImage || (string)go.Tag == "wall"))
                    {
                        GrabWeapon(crew);
                        break;
                    }
                }
            }
        }
    }

    void GrabWeapon(PhysicsObject crew)
    {
        PlasmaCannon weapon = new PlasmaCannon(30, 15);
        crew.Add(weapon);
        weapon.ProjectileCollision += (ammo, target) => {
            ammo.Destroy();
            if (aliens.Contains(target))
            {
                KillAlien(target);
            }
        };

        weapon.IsVisible = false;
        Timer.SingleShot(GRAB_WEAPON_TIME, () => {
            weapon.IsVisible = true;
            ShootWithWeapon(crew, weapon);
        });
    }

    void AlienGrows(Timer timer, PhysicsObject alien)
    {
        GameObject skin = GetObjectSkin(alien);
        if (((string)alien.Tag == "spawn" && skin.Size.MagnitudeSquared < TILE_SIZE * TILE_SIZE * 3.5) ||
            ((string)alien.Tag == "alien" && skin.Size.MagnitudeSquared < TILE_SIZE * TILE_SIZE * 2.5) ||
            ((string)alien.Tag == "queen" /*grow until stop*/))
        {
            // Grow rate
            if ((string)alien.Tag == "spawn")
                skin.Size *= 1.1;
            if ((string)alien.Tag == "alien")
                skin.Size *= 1.05;
            if ((string)alien.Tag == "queen")
                skin.Size *= 1.05;
        }
        else
        {
            if ((string)alien.Tag == "spawn")
            {
                skin.Animation = alienIdleAnimation;
                skin.Size = new Vector(TILE_SIZE * 0.75, TILE_SIZE * 0.75);
                alien.Tag = "alien";
                alien.Size = new Vector(TILE_SIZE * 0.75, TILE_SIZE - 4);

                // Changes collision ignore group, safely!
                QuickHackDoNotStuckOnGrille(alien);

                if (alien == queen)
                    IsHuge = true; // fills precondition
            }
            if (alien == queen && (string)alien.Tag == "alien" && isNesting)
            {
                // Does not fit into thin corridors anymore!
                skin.Animation = queenIdleAnimation;
                skin.Size = new Vector(TILE_SIZE * 0.6, TILE_SIZE * 0.6);
                alien.Tag = "queen";
                alien.Size = new Vector(TILE_SIZE * 1.4, TILE_SIZE * 1.4);
                skin.Size = alien.Size;
                alien.Brain = null;
            }
        }

        // stop growing
        if ((string)alien.Tag == "alien" && (alien != queen || !isNesting) && skin.Size.MagnitudeSquared > TILE_SIZE * TILE_SIZE * 2.0)
        {
            timer.Stop();
            skin.Size = new Vector(TILE_SIZE, TILE_SIZE);
        }
        else if ((string)alien.Tag == "queen" && skin.Size.MagnitudeSquared > TILE_SIZE * TILE_SIZE * 5.0)
        {
            timer.Stop();
            skin.Size = new Vector(TILE_SIZE * 1.5, TILE_SIZE * 1.5);
            CheckForBodiesInTheNest();
        }
    }

    // Sometimes alien grows adult when on grille and gets stuck. This tries to avoid that.
    void QuickHackDoNotStuckOnGrille(PhysicsObject alien)
    {
        bool nearGrille = false;
        foreach(var grilleCandidate in GetObjectsAt(alien.Position, "wall", TILE_SIZE))
        {
            if (grilleCandidate.Image==grilleImage || grilleCandidate.Image==bentGrilleImage )
            {
                nearGrille = true;
            }
        }
        if (!nearGrille)
            alien.CollisionIgnoreGroup = 0;
        else
            Timer.SingleShot(1.0, ()=>QuickHackDoNotStuckOnGrille(alien));
    }

    void GrilleGetsTorn(PhysicsObject collider, PhysicsObject collided)
    {
        PhysicsObject grille = ((string)collider.Tag == "wall" ? collider : collided);
        if (!grille.IgnoresCollisionResponse)
        {
            grille.Image = bentGrilleImage;
            grille.IgnoresCollisionResponse = true;
            grilleSmashSound.Play();
        }
    }

    void CheckForBodiesInTheNest()
    {
        if (!isNesting || queen.IsDestroyed || queen.Tag != "queen")
            return;

        double closestDistance = 0;
        GameObject closestBody = GetClosestBody(queen.Position, out closestDistance);
        
        if (closestBody != null && closestDistance<NEST_SIZE)
        {
            GetObjectSkin(closestBody).Animation.Start();
            Timer.SingleShot(SPAWN_INCUBATION_TIME, () => SpawnHatches(closestBody));
        }
    }

    private GameObject GetClosestBody(Vector position, out double closestDistance)
    {
        closestDistance = Screen.Size.Magnitude;
        GameObject closestBody = null;
        foreach (var body in GetObjects(o => (string)o.Tag == "host"))
        {
            double distance = Vector.Distance(position, body.Position);

            bool isGoodForSpawn =
                body is PhysicsObject && // is PO
                (body as PhysicsObject).IgnoresCollisionResponse == false && // is FRESH
                !GetObjectSkin(body).Animation.IsPlaying; // is not already incubating

            if (isGoodForSpawn && (closestBody == null || distance < closestDistance))
            {
                closestBody = body;
                closestDistance = distance;
            }
        }
        return closestBody;
    }
    void SpawnHatches(GameObject fromHost)
    {
        (fromHost as PhysicsObject).IgnoresCollisionResponse = true;
        GetObjectSkin(fromHost).Animation.Stop();
        PhysicsObject spawn = SpawnSpawn(TILE_SIZE, TILE_SIZE);
        spawn.Position = fromHost.Position;
        MakeCritterWander(spawn, ALIEN_WANDER_SPEED);

        if (!hasShownControlTip)
        {
            hasShownControlTip = true;
            Label controlTip = new Label("You have spawn. Press '1','2',... etc.\n to remote control them with pheromones.");
            controlTip.TextColor = Color.White;
            GameObject tipBackground = new GameObject(controlTip.Width+50, controlTip.Height+50);
            tipBackground.Color = Color.Black;
            Add(tipBackground, 3);
            Add(controlTip, 3);
            Timer.SingleShot(5.0, () => { Remove(controlTip); Remove(tipBackground); });
        }
    }
    #endregion

    #region interaction
    void MoveAlien(Vector direction)
    {
        if ((controlledAlien == queen && !isNesting) || (controlledAlien != null && controlledAlien != queen))
        {
            controlledAlien.Push(direction);

            // Aliens can "nibble" body with them
            double closestDistance = 0;
            GameObject closestBody = GetClosestBody(queen.Position, out closestDistance);
            if (closestBody != null && closestBody is PhysicsObject &&
                closestDistance < TILE_SIZE && RandomGen.NextBool())
            {
                ((PhysicsObject)closestBody).Push(direction / 10);
            }
        }
    }
    void OpenDoor(PhysicsObject door, PhysicsObject collider)
    {
        if (updateTickCounter > 10)
        {
            door.Image = floorImage;
            Timer.SingleShot(2.0, () => CloseDoor(door));
        }
    }
    void CloseDoor(PhysicsObject door)
    {
        if (GetObjectsAt(door.Position, door.Width / 2 - 1.0).Count>1)
        {
            // Still blocked. Wait more.
            Timer.SingleShot(2.0, () => CloseDoor(door));
        }
        else
        {
            door.Image = doorImage;
        }
    }
    void BuildNest()
    {
        //if (!isNesting && true)
        if(!isNesting && isHuge && hasHeat && hasHost)
        {
            isNesting = true;
            Timer growTimer = new Timer();
            growTimer.Interval = ALIEN_GROW_INTERVAL;
            growTimer.Timeout += () => AlienGrows(growTimer, queen);
            growTimer.Start();

            var nestingBrain = new RandomMoverBrain(10);
            nestingBrain.WanderPosition = queen.Position;
            nestingBrain.WanderRadius = 40;
            nestingBrain.Active = true;
            queen.Brain = nestingBrain;
        }
        else
        {
            MessageDisplay.Add("Cannot nest yet or here!");
        }
    }
    void CrewGetsEaten(PhysicsObject collider, PhysicsObject collided)
    {
        PhysicsObject alien;
        PhysicsObject crew;
        if ((string)collider.Tag == "crew")
        {
            alien = collided;
            crew = collider;
        }
        else
        {
            alien = collider;
            crew = collided; 
        }

        // Determnine who gets killed (alien/crew member) and update bookkeeping
        if ((string)alien.Tag == "spawn")
        {
            KillAlien(alien);
        }
        else
        {
            KillCrew(crew);
        }
    }

    private void EndGame()
    {
        Label endGameInfo = null;
        if (crewMembers.Count==0)
        {
            endGameInfo = new Label("Game Over, you win");
            Timer lastOneTurnsOffTheLights = new Timer();
            lastOneTurnsOffTheLights.Interval = 0.1;
            lastOneTurnsOffTheLights.Timeout += () => Level.AmbientLight = Math.Max(0.1, Level.AmbientLight - 0.1);
            lastOneTurnsOffTheLights.Start();
        }
        else if (aliens.Count <= 1 || queen.IsDestroyed)
        {
            endGameInfo = new Label("Game Over, you lose");
            Timer.SingleShot(3.0, () =>
            {
                // Make everything movable by the explosoin
                foreach (var go in GetObjects((o) => true))
                {
                    if (go is PhysicsObject)
                    {
                        PhysicsObject po = (PhysicsObject)go;
                        po.CanRotate = true;
                        po.Mass = 1.0; // makes non-static
                        po.CollisionIgnoreGroup = 1;
                    }
                    else
                    {
                        if (go.Image != null)
                        {
                            PhysicsObject substitute = new PhysicsObject(go.Width, go.Height);
                            substitute.Image = go.Image;
                            substitute.Position = go.Position;
                            Add(substitute);
                            substitute.CollisionIgnoreGroup = 1;
                        }
                        Remove(go);
                    }
                }

                // xplode
                Explosion nuclearBlast = new Explosion(2 * Screen.Width / 3);
                nuclearBlast.Speed = 500;
                Add(nuclearBlast);
            });
        }

        if (endGameInfo != null)
        {
            endGameInfo.TextColor = Color.White;
            Add(endGameInfo,3);
            GameObject egiBackground = new GameObject(endGameInfo.Width+50, endGameInfo.Height+50);
            egiBackground.Color = Color.Black;
            Add(egiBackground,3);
        }
        Timer.SingleShot(6.0, GameOver);
    }


    private void PutPoolOfBlood(Color bloodColor, PhysicsObject whoDies)
    {
        GameObject poolOfBlood = new GameObject(whoDies.Width, whoDies.Height);
        poolOfBlood.Shape = Shape.Circle;
        poolOfBlood.Angle = Angle.RightAngle;
        poolOfBlood.Color = bloodColor;
        poolOfBlood.Position = whoDies.Position;
        poolOfBlood.Tag = "blood";
        Add(poolOfBlood, -2);
    }

    private void KillAlien(PhysicsObject alien)
    {
        Color bloodColor = Color.DarkJungleGreen;
        PhysicsObject whoDies = alien;
        PutPoolOfBlood(bloodColor, whoDies);
        whoDies.Destroy();
        alienDiesSound.Play();
        if (aliens.Contains(whoDies))
        {
            aliens.Remove(whoDies);
        }
        if (alien == controlledAlien)
            controlledAlien = null;

        // End game condition
        if (aliens.Count <= 1 || queen.IsDestroyed || crewMembers.Count == 0)
        {
            EndGame();
        }
    }


    private void KillCrew(PhysicsObject crew)
    {
        Color bloodColor = Color.Darker(Color.BloodRed, 10);
        PhysicsObject whoDies = crew;
        PutPoolOfBlood(bloodColor, whoDies);
        whoDies.Destroy();
        humanDiesSound.Play();

        if (crewMembers.Contains(whoDies))
        {
            // h and w flipped on purpose (prone)
            CreateCrewBody(whoDies.Position, whoDies.Height, whoDies.Width);
            crewMembers.Remove(whoDies);
        }

        // End game condition
        if (aliens.Count == 0 || queen.IsDestroyed || crewMembers.Count == 0)
        {
            EndGame();
        }
    }      

    private PhysicsObject CreateCrewBody(Vector position, double w, double h)
    {
        var body = new PhysicsObject(w, h);
        body.Color = Color.Transparent;
        body.CanRotate = false;
        body.Position = position;
        body.Tag = "host";
        Add(body, -1);
        
        GameObject bodySkin = new GameObject(TILE_SIZE, TILE_SIZE);
        bodySkin.Tag = "skin";
        bodySkin.Animation = new Animation( deadCrewAnimation );
        body.Add(bodySkin);

        body.Restitution = 0.1;
        body.LinearDamping = 0.95;

        return body;
    }
    void GameOver()
    {
        ConfirmExit();
    }
    private void ShootWithWeapon(PhysicsObject crew, Weapon weapon)
    {
        if (!crew.IsDestroyed)
        {
            foreach (var alien in aliens)
            {
                if (crew.SeesObject(alien, go => go.Image == doorImage || (string)go.Tag == "wall"))
                {
                    Vector fromCrewToAlien = alien.Position - crew.Position;
                    weapon.Angle = fromCrewToAlien.Angle + RandomGen.NextAngle(Angle.FromDegrees(-10), Angle.FromDegrees(10));
                    PhysicsObject projectile = weapon.Shoot();    
                    break;
                }
            }
            Timer.SingleShot(1.0 + RandomGen.NextDouble(0.5, 2.0), () => ShootWithWeapon(crew, weapon));
        }
    }

    private void ControlSpawn(int index)
    {
        if (controlledAlien != null && controlledAlien != queen)
        {
            // give it back its brains.
            MakeCritterWander(controlledAlien, ALIEN_WANDER_SPEED);
        }

        if (aliens.Count == 0 || index < 0 || index >= aliens.Count)
            return; // no such alien

        controlledAlien = aliens[index];
        controlledAlien.Brain = null; // You are the brain.
    }
    #endregion

    #region helpers
    private static GameObject GetObjectSkin(GameObject creature)
    {
        GameObject skin = null;
        foreach (var skinCandidate in creature.Objects)
        {
            if ((string)skinCandidate.Tag == "skin")
                skin = skinCandidate;
        }
        return skin;
    }
    private static Weapon GetObjectWeapon(GameObject creature)
    {
        Weapon weapon = null;
        foreach (var weaponCandidate in creature.Objects)
        {
            if (weaponCandidate is Weapon)
                weapon = (Weapon)weaponCandidate;
        }
        return weapon;
    }
    private static void MakeCritterWander(PhysicsObject critter, double wanderSpeed)
    {
        LabyrinthWandererBrain brains = new LabyrinthWandererBrain(16, wanderSpeed);
        brains.LabyrinthWallTag = "wall";
        brains.DirectionChangeTimeout = 3.0;
        critter.Brain = brains;
    }
    #endregion

}
