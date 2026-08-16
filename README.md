# Pauel's Random Fixes

This Nuclear Option mod fixes a whole bunch of stuff. Each fix can be toggled on/off at runtime, either in the config
file (`BepInEx/config/Pauel3312.PauelsRandomFixes.cfg`), or by using the BepInEx Configuration Manager's in game GUI (F1
to open by default).

## Current list of included fixes

#### AbsoluteZoom

> "Zoom View" bind will function as an absolute axis for camera.<br>
> Configurable sensitivity for cockpit/flyby/orbit camera, whether to use negative region of the axis bind (requires
> device's axis to have a negative portion), and whether to invert the zoom axis.
> <br><br>Client only, off by default.
---

#### AllowBindingMouseAxesFix

> Adds ability to rebind axes (under Mouse column) to mouse by moving mouse during assignment.
> <br><br>Client only, on by default.
---

#### BlueprinterServerFix

> Prevents Blueprinter's prefabHash collision reassignments from being cleaned up before full game load on dedicated
> servers, fixing various prefab mix-up issues with too many content mods (e.g. swapped turret vs chassis, wrong
> container
> types spawned). Does nothing without Blueprinter present.
> <br><br>Required on server and client, off by default.
---

#### BrakeAsAxis

> Normally, both "Apply Brakes" and "Brake Axis" binds are binary, any input on either applies 100% braking power. With
> this enabled, the "Brake Axis" bind becomes properly analogue, applying proportional braking force to axis input.
> Has a configuration whether to use negative region of bound axis, which needs to match whether your physical axis bind
> has one or not.
> <br><br>Client only, off by default (to prevent unexpected braking behaviour when negative region is set up wrong).
---

#### ClickthroughChatbox

> Fixes allowing being able to click through Kill Feed / Chatbox to interact with covered up UI elements beneath.
> <br><br>Client only, on by default.
---

#### DisableVerticalCameraInCockpitFix

> When enabled, prevents cockpit camera from moving vertically with vertical camera movement keys ("Move Vertical",
> "Move Up", "Move Down"). Functionality in Free camera and Editor stays the same, only reverting this 0.34 change for
> cockpit camera.
> <br><br>Client only, on by default.
---

#### FPSBoundMouseFix

> Fixed the game incorrectly interpreting various axis reads that are commonly used for mouse input to be considered
> absolute,
> while mouse input is relative, and erroneously applying additional deltaTime multipliers to them, causing these inputs
> (used for things like free look, virtual joystick, map panning) to be more sensitive with lower FPS.
> <br>New rewrite on this fix properly distinguishes input sources between relative and absolute, meaning it can be on
> without
> affecting gamepad/absolute input sources.
> <br><br> Extensive configurable sensitivities for various mouse inputs, including separate X and Y sensitivity for
> Virtual
> Joystick. These stack with vanilla game settings, and allow for additional fine control.<br>
> Also includes an option to allow Centering Force to keep acting on
> Virtual Joystick when using Free Look (normally in vanilla, when holding Free Look your last Virtual Joystick state is
> frozen).
> <br><br>Client only, on by default.
---

#### GunSelfDamageFix

> Fixes fired bullets and lasers sometimes hitting owner's plane, by preventing those being able to collide with the
> owner.
> This can especially happen on servers with higher ping, in certain planes (Brawler's 35mm and Medusa's laser are
> particularly susceptible), flying faster, and manoeuvring aggressively while firing.<br><br>Fix for bullets damaging
> own plane is fully effective on client alone, as that self-impact call is from client. Fix for lasers damaging own
> plane
> is server sided, as laser damage (and its linecast) happens on server.<br><br>With the fix only on server, clients'
> bullet self hits are also rejected, so unfixed clients are still protected against damaging themselves, but that won't
> prevent the client's BulletSim from stopping as it impacted, and thus their bullet disappears and won't damage the
> intended target.
> <br><br>Ideally, present on both client and server, but only on either end still has some benefits, on by default.
---

#### LockedMapControlsWithVJFix

> Fixes all plane control inputs being both prevented and stuck on their last state when using Virtual Joystick and
> opening map (until closing it again). Mouse will keep functioning as normal, but for example other keyboard and
> gamepad
> inputs to control the plane can still happen, and with no inputs the plane's control surfaces revert to neutral on
> map.
> <br><br>Additionally, contains two sub-options (both of these are off by default):
> - Option to Freeze VJ's input while map is open (in this state, the current VJ position is retained on map continuing
    input,
>   but other inputs from e.g. keyboard/controller are still allowed that stack with this)
> - Option to Restore VJ's position to where it was when map is closed
>
> Client only, on by default (does nothing when not using Virtual Joystick).
---

#### LongRangeGunServerValidatorFix

> Fixes server rejecting client player bullet impact calls from shots further than 3000m away (game has a hardcoded
> 3000m
> or 5s bullet limit for HitPlausible), so that player controlled long range guns (e.g. railguns) can do proper damage
> when playing on a server.
> <br><br>Server only, on by default (does nothing on client).
---

#### LookAtTargetFix

> Allows holding "Look At Target" keybind in first person (cockpit) camera to point target to a target, snaps back to
> center
> when released. Similar to what "Center View" does with Gameplay Settings ⇒ Target Padlock on, but that one is a
> toggle,
> while this one is only active as long as you hold the bind down, to easier temporarily look at your target.
> <br><br>Client only, on by default.
---

#### ManualEngineSwivelFix

> When enabled, allows overriding engine swivel system to be toggled between auto vs fully manual, without the game
> trying
> to be "smart" about it and change engine vector whenever it feels like it. In manual mode, the swivel will always stay
> where player points it, unless specifically toggled back to auto mode.
> <br><br>Toggle by holding "Axis Modifier" and press "Toggle Flight Assist" (this will only toggle engine vectoring
> mode,
> not flight assist itself, to allow toggling FA and engine vector mode separately).
> <br><br>Additionally, a second toggle bind can be enabled with "Enable Long Press Toggle Hotkey", which turns on the
> chosen
> "Long Press Toggle Hotkey" (by default Radar) to toggle engine vectoring mode on a long press. The short press of such
> a
> chosen action remains the same. E.g. if this is enabled and set to Radar, a short press on "Toggle Radar" key toggles
> Radar,
> and a long press on this same key toggles engine vectoring instead.
> <br><br>Optionally, you can disable 45 degree swivel limit on low speeds when on manual mode, and auto toggling to
> manual
> vectoring when player inputs on Custom Axis 1 in auto mode, instead of needing to toggle it to manual first (both
> enabled
> by default).
> <br><br>This engine vectoring fix is applicable to both swivel duct system (Vagrant, Medusa), and ducted thrust system
> craft (Vortex). Does not affect tilt-wing (e.g. Tarantula) or wing sweep (e.g. Alkyon).
> <br><br>Client only, off by default.
---

#### RemoveTagsInTTS

> Prevents TTS from reading out HTML tags in messages.
> <br><br>Also allows customising regex blacklist of what to strip (by default <> and [] tags are removed).
> <br><br>Client only, on by default.
---

#### RequireFreelookWithoutVJ

> Enables needing to hold down Free Look button to activate Free Look, even when Virtual Joystick is disabled. While
> using
> Free Look in this mode, releasing it snaps camera back to center.
> <br><br>Client only, off by default.
---

#### TargetDesignatorFix

> Fixes Target Designator indicator on center of screen inconsistently showing depending on weapon selected, and not
> properly updating on gear-up state until weapons are swapped.
> <br><br>Supports configuring to make sure the icon is always on regardless of gear/safety (on by default), and to
> fade out the icon when safety is on (off by default, configurable fade-out opacity).
> <br><br>Client only, on by default.
---

#### ThrottleRelativeVelocity

> Fixes "Throttle Axis" bind to function as analogue input for relative throttle up/down inputs (instead of a binary,
> fixed
> rate up/down motion regardless of how much either side was pressed down). Has configurable sensitivity.
> <br>Also works when changing Custom Axis with throttle when holding "Axis Modifier" key.
> <br><br>Not relevant (or active) if you don't have Relative Throttle on in Controls settings (i.e. when using a
> physical
> throttle slider).
---

#### WarheadDesyncFix

> Fixes warheads showing as available on clients when they're actually inaccessible due to how a warhead storage's 
> selfDisabled state doesn't sync to clients (but is used on server to validate whether nukes in them are available).<br>
> This resulted in spawning with nukes in loadout in an airbase with seemingly plenty available nukes, only to have them 
> stripped upon spawn.
> The fix works by preventing nukes from being able to be placed in disabled warhead storages to begin with, so they're 
> not put in inaccessible places (where not only would clients erroneously see them as accessible, they'd be unusable 
> regardless). Those warheads are instead redistributed to the next valid, accessible storage nearby.
> <br><br>Server only, on by default.