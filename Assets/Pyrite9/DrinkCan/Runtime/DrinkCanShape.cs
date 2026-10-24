using UnityEngine;

namespace Pyrite9.DrinkCan
{
    public enum DrinkCanSizePreset
    {
        Can350 = 0,     // 350 ml standard
        Can500 = 1,     // 500 ml tall
        Slim250 = 2,    // 250 ml slim
        Coffee190 = 3,  // 190 g short coffee can
        Custom = 99,
    }

    public enum DrinkCanLabelFit
    {
        Stretch = 0,    // fill the whole straight body
        KeepAspect = 1, // wrap once around, height follows the image aspect, rest is bare metal
        Repeat = 2,     // repeat around the can so the image keeps (roughly) its aspect at full body height
    }

    /// <summary>Cylinder size in millimetres. Everything else (seam, neck, tab) is derived at real-world size.</summary>
    [System.Serializable]
    public struct DrinkCanShape
    {
        public const float MinHeightMm = 60f, MaxHeightMm = 220f;
        public const float MinDiameterMm = 45f, MaxDiameterMm = 90f;

        public float heightMm;
        public float diameterMm;

        public DrinkCanShape(float heightMm, float diameterMm)
        {
            this.heightMm = heightMm;
            this.diameterMm = diameterMm;
        }

        public DrinkCanShape Clamped => new DrinkCanShape(
            Mathf.Clamp(heightMm, MinHeightMm, MaxHeightMm),
            Mathf.Clamp(diameterMm, MinDiameterMm, MaxDiameterMm));

        // Real-world sizes are approximate (docs/drink-can.md 3절, 확인 필요).
        public static bool TryGetPreset(DrinkCanSizePreset preset, out DrinkCanShape shape)
        {
            switch (preset)
            {
                case DrinkCanSizePreset.Can350: shape = new DrinkCanShape(122f, 66f); return true;
                case DrinkCanSizePreset.Can500: shape = new DrinkCanShape(168f, 66f); return true;
                case DrinkCanSizePreset.Slim250: shape = new DrinkCanShape(133f, 53f); return true;
                case DrinkCanSizePreset.Coffee190: shape = new DrinkCanShape(104f, 53f); return true;
                default: shape = default; return false;
            }
        }
    }
}
