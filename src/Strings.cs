using System;
using System.Globalization;

namespace Brighter
{
    /// <summary>Tiny two-language string table: Turkish on Turkish systems, English everywhere else.</summary>
    internal static class L
    {
        private static readonly bool Turkish = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "tr", StringComparison.OrdinalIgnoreCase);

        private static string T(string tr, string en)
        {
            return Turkish ? tr : en;
        }

        // Main view
        public static string AllMonitors { get { return T("Tüm monitörler", "All monitors"); } }
        public static string AllMonitorsHint { get { return T("Hepsini aynı değere getir", "Set every display to the same level"); } }
        public static string AllMonitorsRelativeHint { get { return T("Aradaki farkı koruyarak birlikte kaydır", "Move all together, keeping their differences"); } }
        public static string Scanning { get { return T("Monitörler taranıyor…", "Scanning monitors…"); } }
        public static string NoMonitors { get { return T("Hiç monitör bulunamadı.", "No monitors were found."); } }
        public static string Primary { get { return T("Birincil", "Primary"); } }
        public static string Internal { get { return T("Dahili", "Internal"); } }
        public static string Virtual { get { return T("Sanal", "Virtual"); } }
        public static string Monitor { get { return T("Monitör", "Monitor"); } }
        public static string BuiltInDisplay { get { return T("Dahili ekran", "Built-in display"); } }
        public static string Software { get { return T("Yazılımsal", "Software"); } }
        public static string SoftwareNote { get { return T("Bu monitör parlaklık komutlarına yanıt vermiyor (DDC/CI kapalı olabilir); parlaklık ekranın üstüne koyu bir katman çizilerek azaltılıyor.", "This monitor does not answer brightness commands (DDC/CI may be off); brightness is lowered by drawing a dark layer over the screen."); } }
        public static string NoAnswer { get { return T("Yanıt yok", "No answer"); } }
        public static string NotResponding { get { return T("Bu monitör şu an donanım komutlarına yanıt vermiyor; arka planda tekrar deneniyor. Bu sırada parlaklık kaydırıcısı ekranı geçici olarak yazılımla karartır (monitörün kendi ayarı değişmez); monitör yanıt verince bu karartma kalkar. Uzun sürerse monitörü güç tuşundan kapatıp aç.", "This monitor is not answering hardware commands right now; retrying in the background. Meanwhile the brightness slider dims the picture in software (the monitor's own setting does not change); that dimming is removed once the monitor answers. If it lasts, power-cycle the monitor."); } }
        public static string Brightness { get { return T("Parlaklık", "Brightness"); } }
        public static string Contrast { get { return T("Kontrast", "Contrast"); } }
        public static string Temperature { get { return T("Sıcaklık", "Warmth"); } }
        public static string Volume { get { return T("Ses", "Volume"); } }
        public static string Input { get { return T("Giriş", "Input"); } }
        public static string InputTip { get { return T("Giriş kaynağını değiştir", "Switch input source"); } }
        public static string PowerTip { get { return T("Monitörü uyut / uyandır", "Put monitor to sleep / wake it"); } }

        // Title bar / tray
        public static string Settings { get { return T("Ayarlar", "Settings"); } }
        public static string Refresh { get { return T("Yenile", "Refresh"); } }
        public static string Minimize { get { return T("Küçült", "Minimize"); } }
        public static string CloseToTray { get { return T("Tepsiye gizle", "Hide to tray"); } }
        public static string Open { get { return T("Aç", "Open"); } }
        public static string Exit { get { return T("Çıkış", "Exit"); } }

        // Settings: arrangement
        public static string Layout { get { return T("Monitör yerleşimi", "Display arrangement"); } }
        public static string LayoutHint { get { return T("Kutuları masandaki dizilişe göre sürükle, sonra Uygula'ya bas. Yıldızlı olan birincil ekrandır.", "Drag the boxes to match your desk, then press Apply. The starred one is the primary display."); } }
        public static string LayoutSingle { get { return T("Yerleşimi düzenlemek için en az iki ekran gerekir.", "Arranging needs at least two displays."); } }
        public static string MakePrimary { get { return T("Seçiliyi birincil yap", "Make selected primary"); } }
        public static string LayoutApplied { get { return T("Uygulandı", "Applied"); } }
        public static string LayoutFailed { get { return T("Windows bu yerleşimi kabul etmedi", "Windows rejected this arrangement"); } }

        // Settings: general
        public static string General { get { return T("Genel", "General"); } }
        public static string AutoStart { get { return T("Windows ile başlat", "Start with Windows"); } }
        public static string AutoStartHint { get { return T("Açılışta tepside gizli başlar", "Starts hidden in the tray at sign-in"); } }
        public static string Restore { get { return T("Monitörü hatırla", "Remember monitors"); } }
        public static string RestoreHint { get { return T("Monitör tekrar takılınca son ayarını geri yükler", "Restores the last values when a monitor is plugged back in"); } }
        public static string RelativeMaster { get { return T("Oranlı toplu ayar", "Relative master slider"); } }
        public static string RelativeMasterHint { get { return T("\"Tüm monitörler\" kaydırıcısı aradaki farkları korur", "The \"All monitors\" slider keeps the differences between displays"); } }

        // Settings: shortcuts
        public static string Hotkeys { get { return T("Klavye kısayolları", "Keyboard shortcuts"); } }
        public static string HotkeysHint { get { return T("Değiştirmek için kutuya tıkla ve yeni kombinasyona bas (Sil: kaldırır)", "Click a box and press the new combination (Delete clears it)"); } }
        public static string HotkeysFailed { get { return T("Bazı kısayollar başka bir program tarafından kullanılıyor", "Some shortcuts are taken by another program"); } }
        public static string KeyUp { get { return T("İmlecin olduğu monitör: artır", "Monitor under cursor: brighter"); } }
        public static string KeyDown { get { return T("İmlecin olduğu monitör: azalt", "Monitor under cursor: dimmer"); } }
        public static string KeyAllUp { get { return T("Tüm monitörler: artır", "All monitors: brighter"); } }
        public static string KeyAllDown { get { return T("Tüm monitörler: azalt", "All monitors: dimmer"); } }
        public static string HotkeyStep { get { return T("Adım (%)", "Step (%)"); } }

        // Settings: profiles
        public static string Profiles { get { return T("Profiller", "Profiles"); } }
        public static string ProfilesHint { get { return T("Şu anki parlaklık, kontrast ve sıcaklık değerlerini bir isimle kaydet; tek tıkla ya da kısayolla uygula.", "Save the current brightness, contrast and warmth under a name; apply with one click or a shortcut."); } }
        public static string Profile { get { return T("Profil", "Profile"); } }
        public static string Apply { get { return T("Uygula", "Apply"); } }
        public static string Delete { get { return T("Sil", "Delete"); } }
        public static string Add { get { return T("Ekle", "Add"); } }
        public static string SaveCurrent { get { return T("Mevcut ayarı kaydet", "Save current"); } }

        // Settings: schedule
        public static string Schedule { get { return T("Saate göre otomatik geçiş", "Switch by time of day"); } }
        public static string ScheduleHint { get { return T("Belirlenen saatte seçili profili uygular (SS:DD)", "Applies the chosen profile at the given time (HH:MM)"); } }
        public static string Day { get { return T("Gündüz", "Day"); } }
        public static string Night { get { return T("Gece", "Night"); } }
        public static string UseSun { get { return T("Gün doğumu / batımını kullan", "Use sunrise / sunset"); } }
        public static string UseSunHint { get { return T("Saatler yerine aşağıdaki konuma göre hesaplanır", "Calculated for the location below instead of the fixed times"); } }
        public static string Latitude { get { return T("Enlem", "Latitude"); } }
        public static string Longitude { get { return T("Boylam", "Longitude"); } }
        public static string SunNeedsLocation { get { return T("Enlem ve boylam gir (örn. 41.01 ve 28.98)", "Enter latitude and longitude (e.g. 51.51 and -0.13)"); } }

        public static string SunToday(string sunrise, string sunset)
        {
            return T("Bugün: gün doğumu " + sunrise + ", gün batımı " + sunset, "Today: sunrise " + sunrise + ", sunset " + sunset);
        }

        // Settings: idle
        public static string Idle { get { return T("Boştayken karart", "Dim when idle"); } }
        public static string IdleHint { get { return T("Klavye ve fare kullanılmayınca parlaklığı aşağıdaki değere indirir, hareket gelince geri alır. Zaten daha kısık olan monitöre dokunmaz.", "When keyboard and mouse are untouched, lowers brightness to the level below and restores it on activity. A monitor that is already dimmer is left alone."); } }
        public static string IdleMedia { get { return T("Video oynarken", "While a video plays"); } }
        public static string IdleMediaOnly { get { return T("Sadece o ekran kararmasın", "Keep only that monitor bright"); } }
        public static string IdleMediaKeepAll { get { return T("Hiçbir ekran kararmasın", "Keep every monitor bright"); } }
        public static string IdleMediaIgnore { get { return T("Yine de karart", "Dim anyway"); } }
        public static string IdleMediaTip { get { return T("Tıkladıkça değişir. Video; sesi çıkan programın penceresinden ya da tam ekran uygulamadan bulunur. Penceresi olmayan arka plan müziği hiçbir ekranı açık tutmaz.", "Click to change. The video is found from the window of the program making sound, or from a full-screen app. Background music with no window keeps no monitor bright."); } }
        public static string IdleMinutes { get { return T("Bekleme (dakika)", "Wait (minutes)"); } }
        public static string IdlePercent { get { return T("İnilecek parlaklık (%)", "Brightness to drop to (%)"); } }

        // Settings: per-application profiles
        public static string AppRules { get { return T("Uygulamaya göre profil", "Profiles per application"); } }
        public static string AppRulesHint { get { return T("Yazdığın program öndeyken seçili profil uygulanır, başka pencereye geçince eski değerler geri gelir. Program adını görev yöneticisindeki gibi yaz (örn. vlc.exe).", "While the named program is in the foreground its profile is applied; switching away brings the previous values back. Use the name shown in Task Manager (e.g. vlc.exe)."); } }

        // Confirmations, tray, about
        public static string Current { get { return T("şu anki", "current"); } }
        public static string RefreshDisplayTip { get { return T("Ekranı yenile: bağlantıyı birkaç saniyeliğine kapatıp açar", "Refresh display: switches the connection off and on again"); } }
        public static string TurnOffTip { get { return T("Monitörü kapat", "Turn monitor off"); } }
        public static string TurnOn { get { return T("Aç", "Turn on"); } }
        public static string DisplayOff { get { return T("Kapalı", "Off"); } }
        public static string Refreshing { get { return T("Yenileniyor…", "Refreshing…"); } }
        public static string NeedTwoDisplays { get { return T("Bunun için en az iki açık ekran gerekir", "This needs at least two active displays"); } }
        public static string DisplayChangeFailed { get { return T("Windows ekran değişikliğini kabul etmedi", "Windows rejected the display change"); } }
        public static string NoOffMonitors { get { return T("Kapalı monitör yok. Bir monitörü kapatmak için \"Açık\" sekmesindeki kartında güç butonuna bas; kapattıkların burada listelenir.", "No monitors are off. Use the power button on a card in the \"On\" tab; the ones you turn off are listed here."); } }

        public static string TabOn(int count)
        {
            return T("Açık monitörler (" + count + ")", "On (" + count + ")");
        }

        public static string TabOff(int count)
        {
            return T("Kapalı monitörler (" + count + ")", "Off (" + count + ")");
        }

        public static string Keep { get { return T("Koru", "Keep"); } }
        public static string Revert { get { return T("Geri al", "Revert"); } }
        public static string SleepAll { get { return T("Tüm monitörleri uyut", "Sleep all monitors"); } }
        public static string AboutHint { get { return T("Bir sorun olursa günlük dosyası ne olduğunu gösterir; hata bildirirken ekleyebilirsin.", "If something goes wrong the log file shows what happened; attach it when reporting a problem."); } }
        public static string OpenLog { get { return T("Günlüğü aç", "Open log"); } }

        public static string LayoutKeepQuestion(int seconds)
        {
            return T("Yeni yerleşim uygulandı. Kalsın mı? " + seconds + " saniye içinde onaylamazsan eski düzene dönülecek.",
                     "The new arrangement is applied. Keep it? Reverting in " + seconds + " seconds unless you confirm.");
        }

        public static string InputKeepQuestion(int seconds)
        {
            return T("Giriş kaynağı değişti. Kalsın mı? " + seconds + " saniye içinde onaylamazsan önceki girişe dönülecek.",
                     "The input source changed. Keep it? Switching back in " + seconds + " seconds unless you confirm.");
        }

        public static string UpdateAvailable(string version)
        {
            return T("Yeni sürüm: " + version, "New version: " + version);
        }

        public static string Status(int total)
        {
            if (Turkish)
                return total + " monitör bulundu";
            return total + (total == 1 ? " monitor found" : " monitors found");
        }
    }
}
