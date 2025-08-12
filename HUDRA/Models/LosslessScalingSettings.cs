using System;

namespace HUDRA.Models
{
    public class LosslessScalingSettings
    {
        public bool UpscalingEnabled { get; set; } = true;
        public LosslessScalingFrameGen FrameGenMultiplier { get; set; } = LosslessScalingFrameGen.Disabled;
        public int FlowScale { get; set; } = 70; // Default from template
        
        public string GetScalingTypeXmlValue() => UpscalingEnabled ? "LS1" : "Off";
        
        public string GetFrameGenXmlValue() => FrameGenMultiplier == LosslessScalingFrameGen.Disabled ? "Off" : "LSFG3";
        
        public string GetFrameGenMultiplierXmlValue() => FrameGenMultiplier switch
        {
            LosslessScalingFrameGen.TwoX => "2",
            LosslessScalingFrameGen.ThreeX => "3", 
            LosslessScalingFrameGen.FourX => "4",
            _ => "2"
        };
        
        public static LosslessScalingSettings FromXml(string scalingType, string frameGeneration, string lsfg3Multiplier, int lsfgFlowScale)
        {
            var settings = new LosslessScalingSettings();
            
            // Parse Upscaling
            settings.UpscalingEnabled = scalingType == "LS1";
            
            // Parse Frame Generation
            if (frameGeneration == "Off")
            {
                settings.FrameGenMultiplier = LosslessScalingFrameGen.Disabled;
            }
            else
            {
                settings.FrameGenMultiplier = lsfg3Multiplier switch
                {
                    "2" => LosslessScalingFrameGen.TwoX,
                    "3" => LosslessScalingFrameGen.ThreeX,
                    "4" => LosslessScalingFrameGen.FourX,
                    _ => LosslessScalingFrameGen.TwoX
                };
            }
            
            // Parse Flow Scale
            settings.FlowScale = Math.Clamp(lsfgFlowScale, 0, 100);
            
            return settings;
        }
    }

    public enum LosslessScalingFrameGen
    {
        Disabled,
        TwoX,
        ThreeX,
        FourX
    }
}