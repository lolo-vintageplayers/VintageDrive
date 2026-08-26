using System;
using System.Collections.Generic;
using VintageDrive.Core.Format;

namespace VintageDrive.Core.Presets
{
    public sealed class ConsolePreset
    {
        public string Key { get; }
        public string Category { get; }
        public string Name { get; }
        public TargetFs Fs { get; }
        public int ClusterBytes { get; }   // 0 = automatique
        public string Notes { get; }       // rappel court (liste compacte)
        public string Pedagogy { get; }    // info-bulle pédagogique (GUI + « presets <clé> »)
        public bool CanFormat { get; }     // false = fiche purement informative (FATX, image à flasher…)

        public ConsolePreset(string key, string category, string name, TargetFs fs, int clusterBytes,
                             string notes, string pedagogy, bool canFormat = true)
        {
            Key = key;
            Category = category;
            Name = name;
            Fs = fs;
            ClusterBytes = clusterBytes;
            Notes = notes;
            Pedagogy = pedagogy;
            CanFormat = canFormat;
        }
    }

    /// <summary>
    /// Réglages par console — la feature des tutos : « choisis ta console, clique Format ».
    /// Chaque preset porte son texte pédagogique (le pourquoi des réglages) ; certains sont
    /// purement informatifs (CanFormat = false) : ils existent pour ÉVITER une bourde
    /// (formater un disque FATX, formater une carte Batocera…). Tout le formatable est MBR :
    /// la seule table de partitions que les consoles comprennent.
    /// </summary>
    public static class ConsolePresets
    {
        public static readonly IReadOnlyList<ConsolePreset> All = new[]
        {
            // ───────────────────────── Nintendo ─────────────────────────
            new ConsolePreset("nes", "Nintendo", "NES / Famicom (EverDrive N8)", TargetFs.Fat32, 32 << 10,
                "",
                "L'EverDrive N8 et le N8 Pro lisent le FAT32. Les ROMs NES se comptent en dizaines de " +
                "kilo-octets : la plus petite carte SD du marché est déjà un océan. FAT32, 32 Ko, MBR — " +
                "et ta Famicom mange des ROMs."),

            new ConsolePreset("snes", "Nintendo", "Super Nintendo (FXPak Pro / SD2SNES)", TargetFs.Fat32, 32 << 10,
                "",
                "Le FXPak Pro (ex-SD2SNES) veut du FAT32. Même les plus gros jeux Super Nintendo font " +
                "6 Mo : la place ne sera jamais ton problème. Réglage sûr sur toutes les révisions de " +
                "la cartouche."),

            new ConsolePreset("n64", "Nintendo", "Nintendo 64 (EverDrive-64, SummerCart64)", TargetFs.Fat32, 32 << 10,
                "",
                "EverDrive-64 (X7 et anciens) et SummerCart64 lisent le FAT32. 32 Ko de cluster pour des " +
                "chargements fluides — une ROM N64 fait 64 Mo maximum, la limite des 4 Go du FAT32 est " +
                "à des années-lumière."),

            new ConsolePreset("gamecube", "Nintendo", "GameCube (Swiss, SD2SP2)", TargetFs.Fat32, 32 << 10,
                "",
                "Swiss lit les cartes SD en FAT32 ; 32 Ko de cluster est le compromis vitesse/compatibilité " +
                "des guides. Bonus : un ISO GameCube fait 1,4 Go maximum, donc la limite des 4 Go du FAT32 " +
                "ne te gênera jamais sur cette console."),

            new ConsolePreset("wii", "Nintendo", "Wii / vWii (USB, SD)", TargetFs.Fat32, 32 << 10,
                "USB Loader GX / WiiFlow",
                "La Wii ne lit que le FAT32, et uniquement sur une table de partitions MBR. Les clusters de 32 Ko " +
                "sont la valeur validée par USB Loader GX et WiiFlow pour une lecture fluide des jeux. " +
                "Héritage du FAT32 : aucun fichier ne peut dépasser 4 Go — les loaders découpent " +
                "automatiquement les gros jeux (.wbfs), tu n'as rien à faire."),

            new ConsolePreset("wiiu", "Nintendo", "Wii U (carte SD homebrew)", TargetFs.Fat32, 32 << 10,
                "ne PAS étiqueter « WIIU » ; disque USB formaté par la console",
                "Deux mondes sur Wii U. La carte SD du homebrew (Aroma) : FAT32, clusters 32 Ko, et surtout " +
                "ne JAMAIS l'étiqueter « WIIU » — ça casse le homebrew. Le disque USB de stockage, lui, est " +
                "formaté PAR la console dans un format chiffré : ton PC ne pourra jamais le lire, c'est " +
                "normal, ce n'est pas une panne."),

            new ConsolePreset("switch", "Nintendo", "Switch (microSD, Atmosphère)", TargetFs.Fat32, 32 << 10,
                "exFAT déconseillé : corruptions",
                "La Switch accepte officiellement l'exFAT via un pilote optionnel… réputé corrompre les " +
                "cartes à la moindre extinction brutale. La scène Atmosphère recommande unanimement le " +
                "FAT32. Seule concession : fichiers de 4 Go max (les gros NSP se découpent)."),

            new ConsolePreset("gb", "Nintendo", "Game Boy / Color / Advance (EverDrive, EZ-Flash)", TargetFs.Fat32, 32 << 10,
                "",
                "EverDrive-GB, EverDrive GBA et EZ-Flash Omega : tous nés en FAT32. Les ROMs vont de " +
                "32 Ko à 32 Mo — une carte de 8 Go avale l'intégralité des catalogues Game Boy, Color " +
                "et Advance réunis."),

            new ConsolePreset("ds", "Nintendo", "DS / DSi (R4, TWiLight Menu, hiyaCFW)", TargetFs.Fat32, 32 << 10,
                "",
                "Les linkers DS (R4 et clones) et le homebrew DSi (TWiLight Menu++, Unlaunch, hiyaCFW) " +
                "exigent du FAT32 — les guides DSi le recommandent explicitement. Certains très vieux " +
                "kernels R4 sont capricieux : si un linker antique boude, réessaie avec une petite carte."),

            new ConsolePreset("3ds", "Nintendo", "2DS / 3DS (SD > 32 Go)", TargetFs.Fat32, 32 << 10,
                "",
                "La 3DS ne lit QUE le FAT32 — et Windows refuse justement de formater en FAT32 au-delà " +
                "de 32 Go. C'est exactement le blocage artificiel que VintageDrive supprime. " +
                "32 Ko de cluster : la recommandation des guides pour de bonnes performances."),

            // ───────────────────────── Sony ─────────────────────────
            new ConsolePreset("ps1", "Sony", "PlayStation (PSIO, XStation)", TargetFs.Fat32, 32 << 10,
                "> 32 Go : PSIO conseille exFAT",
                "Les deux grands ODE PS1 lisent le FAT32. Le manuel PSIO précise : jusqu'à 32 Go → FAT32 ; " +
                "au-delà, il conseille l'exFAT (avec de gros clusters). Le XStation accepte les deux sans " +
                "faire d'histoires. FAT32/32 Ko reste le choix passe-partout des cartes classiques."),

            new ConsolePreset("ps2", "Sony", "PS2 (OPL par USB)", TargetFs.Fat32, 32 << 10,
                "Open PS2 Loader",
                "Open PS2 Loader exige du FAT32 sur le support USB. Les jeux de plus de 4 Go doivent être " +
                "découpés au format .ul — c'est le boulot d'OPL Manager sur PC, pas du formatage. " +
                "Quant au disque dur INTERNE d'une PS2 modée, il utilise le format propriétaire Sony " +
                "(APA/PFS), créé par les outils PS2 (WLE, PFS Shell) — pas par un formatage Windows."),

            new ConsolePreset("ps3", "Sony", "PS3 (disque/clé USB)", TargetFs.Fat32, 32 << 10,
                "fichiers > 4 Go à découper",
                "La PS3 d'origine ne lit que le FAT32 en USB (sauvegardes, mises à jour, packages, films). " +
                "Conséquence : un fichier de plus de 4 Go doit être découpé avant la copie. " +
                "Avec un CFW et webMAN, le NTFS devient possible pour les gros jeux — mais le FAT32 " +
                "reste le format passe-partout qui marche dans tous les cas."),

            new ConsolePreset("ps4", "Sony", "PS4 (clé USB média / PKG)", TargetFs.ExFat, 0,
                "stockage étendu jeux = formaté par la console",
                "La PS4 lit les clés USB en exFAT ou FAT32 — préfère l'exFAT, qui supprime la limite des " +
                "4 Go (indispensable pour les gros PKG du jailbreak GoldHEN). Le « stockage étendu » pour " +
                "les jeux, lui, est formaté PAR la console : illisible sur PC ensuite, c'est normal."),

            new ConsolePreset("psp", "Sony", "PSP (Memory Stick)", TargetFs.Fat32, 32 << 10,
                "",
                "La PSP lit ses Memory Stick (et leurs adaptateurs microSD) en FAT32. Les ISO vont dans " +
                "le dossier ISO/ à la racine, le homebrew dans PSP/GAME/. Un jeu PSP tient toujours " +
                "sous 4 Go : la limite du FAT32 ne pose aucun problème ici."),

            new ConsolePreset("vita", "Sony", "PS Vita / PSTV (SD2Vita)", TargetFs.Fat32, 32 << 10,
                "exFAT toléré, FAT32 plus fiable",
                "L'adaptateur SD2Vita remplace la carte mémoire propriétaire hors de prix de Sony par une " +
                "simple microSD. FAT32 est le choix fiable, reconnu par tous les plugins (StorageMgr, YAMT) ; " +
                "l'exFAT est toléré par les versions récentes mais moins éprouvé. Valable aussi sur PSTV."),

            // ───────────────────────── Sega ─────────────────────────
            new ConsolePreset("sms", "Sega", "Master System / Game Gear / SG-1000 (EverDrive)", TargetFs.Fat32, 32 << 10,
                "",
                "Master EverDrive et EverDrive GG lisent le FAT32 — et le Master EverDrive fait aussi " +
                "tourner les ROMs SG-1000. Des jeux de 8 à 512 Ko : n'importe quelle carte est " +
                "surdimensionnée, prends la plus fiable, pas la plus grosse."),

            new ConsolePreset("megadrive", "Sega", "Mega Drive / Genesis (Mega EverDrive, Mega SD)", TargetFs.Fat32, 32 << 10,
                "les modèles récents lisent aussi l'exFAT",
                "Mega EverDrive (Krikzz) et Mega SD (Terraonion) démarrent tous en FAT32 ; les modèles " +
                "récents (Pro, Mega SD) savent aussi lire l'exFAT. FAT32/32 Ko marche sur toute la gamme " +
                "— et le Mega SD lit en plus les jeux Mega-CD, toujours en FAT32 sans souci."),

            new ConsolePreset("saturn", "Sega", "Saturn (Fenrir, MODE, Satiator, Rhea/Phoebe)", TargetFs.Fat32, 32 << 10,
                "Rhea/Phoebe : FAT32 obligatoire (exFAT = LED figée)",
                "Le FAT32 est le seul format accepté par TOUS les ODE Saturn : Rhea et Phoebe refusent " +
                "net l'exFAT (LED figée, console muette), et le Satiator recommande le FAT32 même sur les " +
                "grosses cartes. Fenrir et MODE tolèrent l'exFAT, mais pourquoi se compliquer : " +
                "FAT32/32 Ko fonctionne sur les quatre."),

            new ConsolePreset("gdemu", "Sega", "Dreamcast (GDEMU)", TargetFs.Fat32, 64 << 10,
                "exFAT REFUSÉ par le GDEMU ; gros clusters recommandés",
                "Le piège classique : Windows formate les grosses SD en exFAT par défaut, et le GDEMU ne " +
                "lit QUE le FAT32 → Dreamcast muette et heures de forum perdues. La doc officielle GDEMU " +
                "recommande en plus les clusters les plus gros possible (64 Ko) pour la fluidité de lecture. " +
                "Ce preset règle les deux pièges d'un clic."),

            // ───────────────────────── Microsoft ─────────────────────────
            new ConsolePreset("xbox", "Microsoft", "Xbox originale (disque interne FATX)", TargetFs.Fat32, 0,
                "FATX propriétaire — pas de formatage Windows",
                "La première Xbox n'utilise ni FAT32 ni NTFS : son disque est en FATX, un format " +
                "propriétaire Microsoft. C'est la console elle-même (softmod, Chimp, XBPartitioner) ou " +
                "l'outil FATXplorer sur PC qui le prépare — un formatage Windows classique ne sert à rien. " +
                "VintageDrive t'est quand même utile AVANT l'installation : teste la capacité réelle et " +
                "la santé du disque d'occasion que tu comptes monter dedans !",
                canFormat: false),

            new ConsolePreset("xbox360", "Microsoft", "Xbox 360 (USB)", TargetFs.Fat32, 32 << 10,
                "",
                "La 360 lit les clés et disques USB en FAT32, puis crée son propre conteneur de données " +
                "dessus. FAT32, 32 Ko, MBR — et la console s'occupe du reste."),

            new ConsolePreset("xboxone", "Microsoft", "Xbox One (clé USB multimédia)", TargetFs.ExFat, 0,
                "disque de jeux = formaté par la console",
                "Pour lire des médias, la One accepte l'exFAT (et le NTFS) — l'exFAT évite la limite des " +
                "4 Go sur les gros fichiers vidéo. Pour STOCKER des jeux, le disque externe est formaté " +
                "par la console dans son propre format : illisible sur PC ensuite, c'est prévu comme ça."),

            // ───────────────────────── Atari ─────────────────────────
            new ConsolePreset("atari", "Atari", "Atari 2600 / 7800 (UnoCart, Concerto)", TargetFs.Fat32, 32 << 10,
                "",
                "Oui, même la 2600 a son preset ! Les cartouches UnoCart-2600 et Concerto (7800) lisent " +
                "une carte SD en FAT32. Les ROMs font 2 à 32 Ko : la plus petite carte du monde est " +
                "déjà cent mille fois trop grande. (La Harmony Cart, elle, se remplit par USB — pas de SD.)"),

            new ConsolePreset("jaguar", "Atari", "Jaguar (Jaguar GameDrive)", TargetFs.Fat32, 32 << 10,
                "pas d'exFAT",
                "Le GameDrive de RetroHQ lit le FAT16 et le FAT32 — pas l'exFAT. FAT32/32 Ko sur la " +
                "microSD et le fauve est nourri, cartouches comme images Jaguar CD."),

            new ConsolePreset("lynx", "Atari", "Lynx (Lynx GameDrive)", TargetFs.Fat32, 32 << 10,
                "pas d'exFAT",
                "Même maison, même règle que sur Jaguar : le Lynx GameDrive de RetroHQ accepte FAT16 et " +
                "FAT32, pas l'exFAT. FAT32/32 Ko et la Lynx repart en tournée."),

            // ───────────────────────── NEC / SNK ─────────────────────────
            new ConsolePreset("pce", "NEC / SNK", "PC-Engine / TurboGrafx-16 (Turbo EverDrive, SSDS3)", TargetFs.Fat32, 32 << 10,
                "",
                "Turbo EverDrive (Krikzz) et Super SD System 3 (Terraonion) lisent le FAT32 — et le " +
                "SSDS3 fait aussi tourner les jeux CD-ROM². FAT32/32 Ko couvre toute la famille, " +
                "TurboGrafx-16 américaine comprise."),

            new ConsolePreset("neogeo", "NEC / SNK", "Neo Geo AES / MVS (NeoSD)", TargetFs.Fat32, 32 << 10,
                "cartes ≤ 32 Go ; ROMs à convertir en .neo",
                "Les cartouches NeoSD et NeoSD Pro de Terraonion exigent du FAT32 (cartes jusqu'à 32 Go). " +
                "Les ROMs doivent d'abord être converties au format .neo avec l'outil PC du fabricant. " +
                "Conseil de Terraonion : privilégie les cartes SanDisk, évite les Samsung."),

            // ───────────────────────── Divers ─────────────────────────
            new ConsolePreset("everdrive", "Divers", "Autres EverDrive et flashcarts", TargetFs.Fat32, 32 << 10,
                "le réflexe FAT32/32 Ko",
                "Pour toute cartouche à carte SD absente de la liste : commence par FAT32/32 Ko, c'est " +
                "le standard de fait du monde flashcart depuis quinze ans. Les modèles récents lisent " +
                "souvent aussi l'exFAT (confirmé par Krikzz), mais autant partir de ce qui marche partout."),

            new ConsolePreset("mister", "Divers", "MiSTer FPGA", TargetFs.ExFat, 0,
                "",
                "L'exception de la bande : le MiSTer tourne sous Linux et sa carte SD se formate en exFAT. " +
                "Aucune limite de 4 Go — pratique pour les grosses images CD (PSX, Saturn, PC-Engine CD)."),

            new ConsolePreset("batocera", "Divers", "Batocera / Recalbox / RetroPie", TargetFs.Fat32, 0,
                "s'installe avec un IMAGEUR, pas un formatage",
                "Piège fréquent : ces systèmes ne s'installent PAS par un formatage. On écrit une image " +
                "complète sur la carte avec un imageur (balenaEtcher, Raspberry Pi Imager…), qui crée " +
                "lui-même ses partitions. Formater avant ne sert à rien, formater après efface tout. " +
                "VintageDrive te sert AVANT : vérifier que la carte n'est pas une fausse capacité !",
                canFormat: false),

            new ConsolePreset("pc", "Divers", "PC moderne (échange de fichiers)", TargetFs.ExFat, 0,
                "fichiers > 4 Go OK",
                "Pour échanger des fichiers entre PC, Mac, box et TV d'aujourd'hui : exFAT — lisible " +
                "partout, sans la limite des 4 Go du FAT32. C'est le bon format pour une clé moderne… " +
                "et le mauvais pour presque toutes les consoles rétro."),
        };

        public static ConsolePreset? Find(string key)
        {
            foreach (var p in All)
                if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }
    }
}
