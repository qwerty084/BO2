using System;
using System.Collections.Generic;

namespace BO2.Services
{
    internal static class WeaponDisplayNameResolver
    {
        private static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // BO2 Zombies aliases from the public Plutonium weapon table:
                // https://github.com/AkbarHashimi/BO2-Plutonium-Modding-Guide/wiki/Weapons-List
                ["870mcs_zm"] = "Remington 870 MCS",
                ["ak47_zm"] = "AK-47",
                ["ak74u_extclip_zm"] = "AK74u",
                ["ak74u_zm"] = "AK74u",
                ["an94_zm"] = "AN-94",
                ["ballista_zm"] = "Ballista",
                ["barretm82_zm"] = "Barrett M82A1",
                ["beacon_zm"] = "G-Strike",
                ["beretta93r_extclip_zm"] = "B23R",
                ["beretta93r_zm"] = "B23R",
                ["blundergat_zm"] = "Blundergat",
                ["blundersplat_bullet_zm"] = "Acid Gat",
                ["blundersplat_explosive_dart_zm"] = "Acid Gat",
                ["blundersplat_zm"] = "Acid Gat",
                ["bouncing_tomahawk_zm"] = "Hell's Retriever",
                ["bowie_knife_zm"] = "Bowie Knife",
                ["c96_zm"] = "Mauser C96",
                ["claymore_zm"] = "Claymore",
                ["cymbal_monkey_zm"] = "Monkey Bomb",
                ["dsr50_zm"] = "DSR 50",
                ["electrocuted_hands_zm"] = "Electrocuted Hands",
                ["emp_grenade_zm"] = "EMP Grenade",
                ["equip_dieseldrone_zm"] = "Maxis Drone",
                ["equip_electrictrap_zm"] = "Electric Trap",
                ["evoskorpion_zm"] = "Skorpion EVO",
                ["fivesevendw_zm"] = "Five-seven Dual Wield",
                ["fivesevenlh_zm"] = "Five-seven Dual Wield",
                ["fiveseven_zm"] = "Five-seven",
                ["fnfal_zm"] = "FAL",
                ["frag_grenade_zm"] = "Frag Grenade",
                ["galil_zm"] = "Galil",
                ["hamr_zm"] = "HAMR",
                ["hk416_zm"] = "M27",
                ["jetgun_zm"] = "Jet Gun",
                ["judge_zm"] = "Executioner",
                ["kard_zm"] = "KAP-40",
                ["knife_ballistic_bowie_zm"] = "Ballistic Knife",
                ["knife_ballistic_no_melee_zm"] = "Ballistic Knife",
                ["knife_ballistic_zm"] = "Ballistic Knife",
                ["ksg_zm"] = "KSG",
                ["lsat_zm"] = "LSAT",
                ["m14_zm"] = "M14",
                ["m16_zm"] = "Colt M16A1",
                ["m1911_zm"] = "M1911",
                ["m32_zm"] = "War Machine",
                ["mg08_zm"] = "MG08/15",
                ["minigun_alcatraz_zm"] = "Death Machine",
                ["mp40_stalker_zm"] = "MP-40",
                ["mp40_zm"] = "MP-40",
                ["mp44_zm"] = "STG-44",
                ["mp5k_zm"] = "MP5",
                ["pdw57_zm"] = "PDW-57",
                ["python_zm"] = "Python",
                ["qcw05_zm"] = "Chicom CQB",
                ["ray_gun_zm"] = "Ray Gun",
                ["raygun_mark2_zm"] = "Ray Gun Mark II",
                ["rnma_zm"] = "Remington New Model Army",
                ["rottweil72_zm"] = "Olympia",
                ["rpd_zm"] = "RPD",
                ["saiga12_zm"] = "S12",
                ["saritch_zm"] = "SMR",
                ["scar_zm"] = "SCAR-H",
                ["slowgun_zm"] = "Paralyzer",
                ["slipgun_zm"] = "Sliquifier",
                ["srm1216_zm"] = "M1216",
                ["sticky_grenade_zm"] = "Semtex",
                ["svu_zm"] = "SVU-AS",
                ["tar21_zm"] = "MTAR",
                ["tazer_knuckles_zm"] = "Galvaknuckles",
                ["thompson_zm"] = "M1927",
                ["time_bomb_detonator_zm"] = "Time Bomb",
                ["time_bomb_zm"] = "Time Bomb",
                ["type95_zm"] = "Type 25",
                ["upgraded_tomahawk_zm"] = "Hell's Redeemer",
                ["usrpg_zm"] = "RPG",
                ["uzi_zm"] = "Uzi",
                ["xm8_zm"] = "M8A1",
            };

        public static string ResolveDisplayName(string weaponAlias)
        {
            string normalizedAlias = weaponAlias.Trim();
            return DisplayNames.TryGetValue(normalizedAlias, out string? displayName)
                ? displayName
                : normalizedAlias;
        }

        public static string FormatForEvent(string weaponAlias)
        {
            string normalizedAlias = weaponAlias.Trim();
            string displayName = ResolveDisplayName(normalizedAlias);
            if (string.Equals(displayName, normalizedAlias, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedAlias;
            }

            return $"{displayName} ({normalizedAlias})";
        }
    }
}
