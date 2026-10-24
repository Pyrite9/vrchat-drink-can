namespace Pyrite9.DrinkCan
{
    static class DrinkCanConst
    {
        public const string Version = "0.1.0";
        public const string Prefix = "Pyrite9/DrinkCan/";

        // synced
        public const string Out = Prefix + "Out";            // bool: can in hand
        public const string Opened = Prefix + "Opened";      // bool
        public const string Level = Prefix + "Level";        // int: sips left
        public const string Drink = Prefix + "Drink";        // bool: menu button, can goes to the mouth (seen by everyone)
        public const string SwapHand = Prefix + "SwapHand";  // bool: menu toggle, can in the other hand than the inspector's spawn hand
        // local
        public const string OpenBtn = Prefix + "OpenBtn";    // bool: menu button
        public const string TabTouch = Prefix + "TabTouch";  // bool: index finger on the tab (contact)
        public const string LipTouch = Prefix + "LipTouch";  // bool: opening at the mouth (contact)
        public const string IsLocal = "IsLocal";
        // each client: VRC Raycast from the hand straight down (DrinkCanThrow) -> <Floor>_Hit, <Floor>_Distance
        public const string Floor = Prefix + "Floor";
        public const string FloorHit = Floor + "_Hit";
        public const string FloorDistance = Floor + "_Distance";
        // each client: two PhysBone sensors on the hand (DrinkCanThrow.BuildFlickSensor) -> <Flick>_Angle; the owner's flick of
        // an empty can throws it (FX Flick layer drives Out off)
        public const string FlickA = Prefix + "FlickA";
        public const string FlickB = Prefix + "FlickB";
        public const string FlickAAngle = FlickA + "_Angle";
        public const string FlickBAngle = FlickB + "_Angle";
        // each client: a hanging PhysBone in the can (DrinkCanPlugin.LevelLiquid) -> <Tilt>_Angle = the can's tilt / 180 deg
        public const string Tilt = Prefix + "Tilt";
        public const string TiltAngle = Tilt + "_Angle";

        public const string MouthTag = "Pyrite9DrinkCanMouth";

        // object names under the DrinkCan root (animation paths are relative to it)
        public const string Hold = "Hold";
        public const string GripR = "GripR";         // grip point on the right hand (Bone Proxy)
        public const string GripL = "GripL";         // grip point on the left hand (Bone Proxy)
        public const string HandPoint = "HandPoint"; // follows GripR or GripL (VRC Parent Constraint, weights moved by the Hand layer)
        public const string HeadPivot = "HeadPivot";  // under the neck at the head joint, copies the head's rotation (first-person safe)
        public const string MouthPoint = "MouthPoint";
        public const string MouthSender = "MouthSender";
        public const string Sound = "Sound";         // under Hold: AudioSource the FX layers play into (VRC Animator Play Audio)
        public const string Throw = "Throw";         // under HandPoint: mesh particle emitters for the thrown can (F5) — metal
        public const string ThrowLabel = "ThrowLabel"; // — label (one submesh per particle mesh, emitted in lockstep)
        public const string DropRay = "DropRay";     // follows HandPoint (position), VRC Raycast down
        public const string DropPoint = "DropPoint"; // the ray's hit on the floor: drop sound source
        public const string FlickSensor = "FlickSensor"; // under HandPoint: the two PhysBone sensors for a flick (throw)
        public const string LiquidMask = "LiquidMask";   // under the can bone: writes the stencil the liquid shows through
        public const string Inside = "Inside";           // under the can bone: the dark inner wall and bottom, seen only through the opening (9-59)
        public const string FoamSurface = "FoamSurface"; // under Hold: the foam layer on the drink (beer head, 9-65)
        public const string Foam = "Foam";               // under the can bone at the opening: fizz when opened (9-65)
        public const string FoamDome = "FoamDome";       // under the can bone at the opening: the beer head welling up when opened (9-66)
        public const string LiquidSurface = "LiquidSurface"; // under Hold: the liquid's own renderer (its colour is animated, 9-54)
        public const string LiquidLevel = "LiquidLevel"; // under the can bone at the inside's middle, kept level: the liquid bone hangs under it
        public const string TiltSensor = "TiltSensor";   // under the can bone: the hanging PhysBone

        // Sounds/ (Freesound CC0, Sounds/CREDITS.txt) — by GUID so a moved package folder still finds them
        public const string OpenSoundGuid = "a2170a6ae3214865a0628e8126309d29";   // DrinkCan_Open.wav
        public const string GulpSoundGuid = "ed45b0598077437e90c243a700dc70bb";   // DrinkCan_Gulp.wav
        public const string DropSoundGuid = "02b6dfb64073403e84465a7a88680dd6";   // DrinkCan_Drop.wav (throw landing, F5)
        // Icons/ — Expression Menu icons, 256 white silhouettes (Source/tools/make_menu_icons.py, 9-83)
        public const string MenuIconGuid = "8a7c759955fb423fb7d1d9e4752c7ce7";        // DrinkCan_Menu.png (the submenu)
        public const string TakeOutIconGuid = "c1e9becff5e147b1924fe52d026531fb";     // DrinkCan_TakeOut.png
        public const string OpenIconGuid = "704887f0175245108de5126f14803a31";        // DrinkCan_Open.png
        public const string DrinkIconGuid = "cf93e5d65d764fb692e518327246c555";       // DrinkCan_Drink.png
        public const string SwitchHandIconGuid = "db5ac2f7a98c4ba7af2b672116e2c5f8";  // DrinkCan_SwitchHand.png
        public const string FoamLayerGuid = "47e99a32f8ff474a88b81ae80c46cbb0";  // Textures/DrinkCan_FoamLayer.png (foam layer and dome, 9-66)
        public const string FoamMatcapGuid = "5ca411dd6a5149e1a26be38a008f5a14"; // Textures/DrinkCan_FoamMatcap.png (dome shading, 9-66)
        public const string FoamDotGuid = "e54c0f840f9b421caadde1aa42478ba5";    // Textures/DrinkCan_FoamDot.png (fizz droplets, 9-66)
        public const string LiquidMaskMaterialGuid = "3d99229586f41de4d84aa4af2b77bacd";   // Materials/DrinkCan_LiquidMask.mat (stencil writer, 9-49)

        public const string LogPath = "Logs/Pyrite9_DrinkCan_build.txt";

        // Expression Menu labels (English / Japanese / Korean = DrinkCanMenuLanguage order).
        // MA uses the menu object's name as the submenu label.
        public static string MenuName(int l) => T(l, "Drink Can", "ドリンク缶", "음료 캔");
        public static string TakeOutLabel(int l) => T(l, "Take out", "取り出す", "꺼내기");
        public static string OpenLabel(int l) => T(l, "Open", "開ける", "따기");
        public static string DrinkLabel(int l) => T(l, "Drink", "飲む", "마시기");
        public static string SwitchHandLabel(int l) => T(l, "Switch hand", "持ち替え", "손 바꾸기");

        public static string T(int l, string en, string jp, string ko) => l == 1 ? jp : l == 2 ? ko : en;
    }
}
