using fftivc.quickerintro.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace fftivc.quickerintro.Configuration
{
    public class Config : Configurable<Config>
    {        
        [DisplayName("Auto Mode Selection Enabled")]
        [Description("Select Enhanced or Classic automatically.")]
        [DefaultValue(true)]
        public bool AutoModeSelectionEnabled { get; set; } = true;

        [DisplayName("Auto Mode Selection")]
        [Description("Enhanced or Classic mode.")]
        [DefaultValue(GameModeSelection.Enhanced)]
        public GameModeSelection AutoModeSelection { get; set; } = GameModeSelection.Enhanced;

        [DisplayName("Skip Intro Movie Enabled")]
        [Description("Skips the Intro Movie")]
        [DefaultValue(true)]
        public bool SkipIntroMovieEnabled { get; set; } = true;

        [DisplayName("Auto Continue Enabled")]
        [Description("Automatically activates the Continue option on the main menu.")]
        [DefaultValue(false)]
        public bool AutoContinueEnabled { get; set; } = false;

        [DisplayName("Auto Open Load Game Menu")]
        [Description("Automatically opens the Load Game menu from the main menu.")]
        [DefaultValue(false)]
        public bool AutoOpenLoadGameMenuEnabled { get; set; } = false;

        [DisplayName("Auto Load Last Save Enabled")]
        [Description("Automatically loads the last manual save once the Load Game menu has opened.")]
        [DefaultValue(false)]
        public bool AutoLoadLastSaveEnabled { get; set; } = false;

        [DisplayName("Debug Logging Enabled")]
        [Description("Writes verbose diagnostic messages to the Reloaded log.")]
        [DefaultValue(false)]
        public bool DebugLoggingEnabled { get; set; } = false;

        public enum GameModeSelection
        {
            [Display(Name = "None")] None,
            [Display(Name = "Enhanced")] Enhanced,
            [Display(Name = "Classic")] Classic
        }
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
        // 
    }
}
