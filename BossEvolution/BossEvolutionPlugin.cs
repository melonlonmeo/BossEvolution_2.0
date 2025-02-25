using TerrariaApi.Server;
using TShockAPI;
using Terraria;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Terraria.DataStructures;
using System.IO;

namespace BossEvolution
{
    [ApiVersion(2, 1)]
    public class BossEvolutionPlugin : TerrariaPlugin
    {
        private static readonly Random random = new Random();
        private Dictionary<int, int[]> currentSkills = new Dictionary<int, int[]>();
        private Dictionary<int, int[]> skillCooldowns = new Dictionary<int, int[]>();
        private const int COOLDOWN_TIME = 30; // 0.5 giây (30 frames)
        private Dictionary<int, string> bossNicknames = new Dictionary<int, string>();
        private Dictionary<int, List<BossSkillPattern>> bossSkillPatterns = new Dictionary<int, List<BossSkillPattern>>();
        private Dictionary<int, List<BossPhase>> bossPhases = new Dictionary<int, List<BossPhase>>();
        private Dictionary<int, float[]> customAI = new Dictionary<int, float[]>();
        private const float PHASE2_HP = 0.5f; // 50% HP
        private const float PHASE3_HP = 0.3f; // 30% HP

        // Thêm danh sách boss biết bay và boss triệu hồi
        private readonly HashSet<int> flyingBosses = new HashSet<int>
        {
            NPCID.EyeofCthulhu,
            NPCID.QueenBee,
            NPCID.TheDestroyer,
            NPCID.Spazmatism,
            NPCID.Retinazer,
            NPCID.DukeFishron,
            NPCID.MoonLordHead,
            NPCID.HallowBoss,     // Empress of Light
            NPCID.QueenSlimeBoss, // Queen Slime
            NPCID.DD2Betsy,
            NPCID.PirateShip,
            NPCID.IceQueen,
            NPCID.MartianSaucer
        };

        private readonly HashSet<int> summonerBosses = new HashSet<int>
        {
            NPCID.BrainofCthulhu,  // Creeper
            NPCID.QueenBee,        // Bees
            NPCID.TheDestroyer,    // Probes
            NPCID.Plantera,        // Tentacles
            NPCID.Golem,           // Fists
            NPCID.DukeFishron,     // Sharkrons
            NPCID.DD2DarkMageT1,
            NPCID.Pumpking,
            NPCID.SantaNK1
        };

        public override string Name => "Boss Evolution";
        public override string Author => "GILX_DevTERRARIAVUI";
        public override string Description => "Allows bosses to evolve with random skills";

        public BossEvolutionPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            ServerApi.Hooks.NpcSpawn.Register(this, OnNPCSpawn);
            ServerApi.Hooks.GameUpdate.Register(this, OnNPCAI);
            ServerApi.Hooks.NpcKilled.Register(this, OnNPCKilled);
            InitializeBossSkills();
        }

        private void OnNPCSpawn(NpcSpawnEventArgs args)
        {
            NPC npc = Main.npc[args.NpcId];
            if (npc.boss)
            {
                RandomizeSkills(npc);
                if (!currentSkills.ContainsKey(npc.whoAmI))
                {
                    currentSkills.Add(npc.whoAmI, new int[8]); // Tối đa 8 skills
                }
                GenerateBossNickname(npc, currentSkills[npc.whoAmI]);
            }
        }

        private void OnNPCAI(EventArgs args)
        {
            try
            {
                foreach(NPC npc in Main.npc)
                {
                    if (npc.boss && npc.active)
                    {
                        Player target = Main.player[npc.target];
                        UpdateCooldowns(npc);
                        ApplySkills(npc, target);
                    }
                }
            }
            catch (Exception)
            {
                // Log lỗi nếu cần
            }
        }

        private void OnNPCKilled(NpcKilledEventArgs args)
        {
            NPC npc = Main.npc[args.npc.whoAmI];
            if (npc.boss)
            {
                // Tính toán độ khó tổng của boss dựa trên các kỹ năng
                if (bossPhases.ContainsKey(npc.whoAmI))
                {
                    float totalDifficulty = 0;
                    int skillCount = 0;

                    foreach (var phase in bossPhases[npc.whoAmI])
                    {
                        foreach (var skill in phase.PhaseSkills)
                        {
                            totalDifficulty += skill.Difficulty;
                            skillCount++;
                        }
                    }

                    // Tính trung bình độ khó
                    float avgDifficulty = totalDifficulty / skillCount;
                    
                    // Tăng tiền dựa vào độ khó trung bình
                    float multiplier = 1.0f + (avgDifficulty * 0.5f); // Ví dụ: độ khó 5 sẽ cho x3.5 tiền
                    npc.value *= multiplier;
                }

                // Dọn dẹp data của boss
                currentSkills.Remove(npc.whoAmI);
                skillCooldowns.Remove(npc.whoAmI);
                customAI.Remove(npc.whoAmI);
                bossPhases.Remove(npc.whoAmI);
                bossNicknames.Remove(npc.whoAmI);
            }
        }

        private void RandomizeSkills(NPC boss)
        {
            if (currentSkills.ContainsKey(boss.whoAmI))
            {
                currentSkills.Remove(boss.whoAmI);
                skillCooldowns.Remove(boss.whoAmI);
                customAI.Remove(boss.whoAmI);
                bossPhases.Remove(boss.whoAmI);
            }

            // Random skills cho từng phase
            var allBosses = GetBossList().Where(x => x != boss.type).ToList();
            
            // Kiểm tra boss là hardmode hay không
            bool isHardmodeBoss = IsHardmodeBoss(boss.type);
            
            // Số kỹ năng cho mỗi phase
            int phase1Skills = 5;
            int phase2Skills = isHardmodeBoss ? 6 : 6;
            int phase3Skills = isHardmodeBoss ? 8 : 7;

            // Random skills cho từng phase
            var phase1 = RandomizePhaseSkills(allBosses, phase1Skills);
            var phase2 = RandomizePhaseSkills(allBosses, phase2Skills);
            var phase3 = RandomizePhaseSkills(allBosses, phase3Skills);

            // Lưu skills vào currentSkills
            var allSkills = new List<BossSkillPattern>();
            allSkills.AddRange(phase1);
            allSkills.AddRange(phase2);
            allSkills.AddRange(phase3);
            
            currentSkills.Add(boss.whoAmI, allSkills.Select(s => s.BossType).ToArray());
            customAI.Add(boss.whoAmI, new float[4]);

            // Lưu phases
            bossPhases[boss.whoAmI] = new List<BossPhase>
            {
                new BossPhase { 
                    HealthPercentage = 1.0f,
                    PhaseSkills = phase1
                },
                new BossPhase { 
                    HealthPercentage = PHASE2_HP,
                    PhaseSkills = phase2
                },
                new BossPhase { 
                    HealthPercentage = PHASE3_HP,
                    PhaseSkills = phase3
                }
            };

            // Khởi tạo cooldowns
            skillCooldowns.Add(boss.whoAmI, new int[isHardmodeBoss ? 8 : 7]);

            // Kiểm tra xem boss có skill của boss biết bay không
            bool hasFlyingSkill = phase1.Any(s => flyingBosses.Contains(s.BossType)) ||
                                 phase2.Any(s => flyingBosses.Contains(s.BossType)) ||
                                 phase3.Any(s => flyingBosses.Contains(s.BossType));

            // Kiểm tra xem boss có skill triệu hồi không
            bool hasSummonSkill = phase1.Any(s => summonerBosses.Contains(s.BossType)) ||
                                 phase2.Any(s => summonerBosses.Contains(s.BossType)) ||
                                 phase3.Any(s => summonerBosses.Contains(s.BossType));

            // Lưu thông tin vào customAI
            customAI[boss.whoAmI] = new float[4] {
                hasFlyingSkill ? 1f : 0f,  // AI[0]: Có thể bay
                hasSummonSkill ? 1f : 0f,  // AI[1]: Có thể điều khiển minion
                0f,                        // AI[2]: Reserved
                0f                         // AI[3]: Reserved
            };
        }

        private List<BossSkillPattern> RandomizePhaseSkills(List<int> bossList, int skillCount)
        {
            var skills = new List<BossSkillPattern>();
            for (int i = 0; i < skillCount; i++)
            {
                int randomBossIndex = random.Next(bossList.Count);
                int bossType = bossList[randomBossIndex];
                
                if (bossSkillPatterns.ContainsKey(bossType))
                {
                    var bossSkills = bossSkillPatterns[bossType];
                    if (bossSkills.Count > 0)
                    {
                        // Lọc bỏ các kỹ năng dịch chuyển nếu là Wall of Flesh
                        var availableSkills = bossSkills;
                        if (bossType == NPCID.WallofFlesh)
                        {
                            availableSkills = bossSkills.Where(s => 
                                !s.SkillName.Contains("Teleport") && 
                                !s.SkillName.Contains("Dash") &&
                                !s.SkillName.Contains("Jump") &&
                                !s.SkillName.Contains("Charge")
                            ).ToList();
                        }

                        if (availableSkills.Count > 0)
                        {
                            int randomSkillIndex = random.Next(availableSkills.Count);
                            skills.Add(availableSkills[randomSkillIndex]);
                        }
                    }
                }
            }
            return skills;
        }

        private void UpdateCooldowns(NPC boss)
        {
            if (!skillCooldowns.ContainsKey(boss.whoAmI)) return;

            var cooldowns = skillCooldowns[boss.whoAmI];
            for (int i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > 0)
                {
                    cooldowns[i]--;
                }
            }
        }

        private void ApplySkills(NPC boss, Player target)
        {
            if (!bossPhases.ContainsKey(boss.whoAmI)) return;
            if (target == null || !target.active || target.dead) return;

            float healthPercentage = (float)boss.life / boss.lifeMax;
            float distanceToTarget = Vector2.Distance(boss.Center, target.Center);
            
            // Lấy phase hiện tại dựa vào HP
            var currentPhase = bossPhases[boss.whoAmI]
                .OrderByDescending(p => p.HealthPercentage)
                .FirstOrDefault(p => healthPercentage <= p.HealthPercentage);

            if (currentPhase == null) return;

            // Tăng tỉ lệ sử dụng skill khi:
            // 1. HP thấp
            // 2. Khoảng cách với target phù hợp
            // 3. Boss đang trong phase cao hơn
            for (int i = 0; i < currentPhase.PhaseSkills.Count; i++)
            {
                if (!skillCooldowns.ContainsKey(boss.whoAmI)) continue;
                
                var cooldowns = skillCooldowns[boss.whoAmI];
                if (i >= cooldowns.Length || cooldowns[i] > 0) continue;

                var skill = currentPhase.PhaseSkills[i];
                int baseChance = 180; // Cơ bản 1/180 mỗi frame

                // Giảm baseChance (tăng tỉ lệ) dựa vào điều kiện
                if (healthPercentage < 0.5f) baseChance = (int)(baseChance * 0.8f); // HP < 50%
                if (healthPercentage < 0.3f) baseChance = (int)(baseChance * 0.7f); // HP < 30%
                
                // Tăng tỉ lệ nếu ở gần target (300-600 pixels)
                if (distanceToTarget > 300 && distanceToTarget < 600)
                {
                    baseChance = (int)(baseChance * 0.9f);
                }

                // Tăng tỉ lệ theo phase
                if (healthPercentage <= PHASE2_HP) baseChance = (int)(baseChance * 0.85f);
                if (healthPercentage <= PHASE3_HP) baseChance = (int)(baseChance * 0.7f);

                // Tăng tỉ lệ theo độ khó của skill
                baseChance = (int)(baseChance * (1f - skill.Difficulty * 0.1f));

                // Kiểm tra xem có thể dùng skill không
                if (Main.rand.Next(baseChance) == 0)
                {
                    Console.WriteLine($"[Boss Evolution] {bossNicknames[boss.whoAmI]} uses {skill.SkillName}!");
                    Console.WriteLine($"[Boss Evolution] Chance: 1/{baseChance} (HP: {healthPercentage*100}%, Distance: {distanceToTarget})");
                    skill.ExecuteSkill(boss, target);
                    cooldowns[i] = COOLDOWN_TIME;
                    
                    // Giảm cooldown khi HP thấp
                    if (healthPercentage < 0.3f) cooldowns[i] = (int)(COOLDOWN_TIME * 0.7f);
                    break;
                }
            }

            // Xử lý khả năng bay nếu boss không biết bay
            if (!flyingBosses.Contains(boss.type) && customAI[boss.whoAmI][0] == 1f)
            {
                // Cho phép boss nhảy cao hơn khi ở trên mặt đất
                if (boss.velocity.Y == 0 && Main.rand.Next(120) == 0)
                {
                    boss.velocity.Y = -12f; // Nhảy cao hơn bình thường
                    Console.WriteLine($"[Boss Evolution] {bossNicknames[boss.whoAmI]} performs a high jump!");
                }
            }

            // Xử lý điều khiển minion
            if (customAI[boss.whoAmI][1] == 1f)
            {
                // Tìm các minion trong phạm vi
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC minion = Main.npc[i];
                    if (!minion.active) continue;

                    // Kiểm tra xem có phải minion không và thuộc về boss này không
                    if (IsMinion(minion.type) && Vector2.Distance(boss.Center, minion.Center) < 800f)
                    {
                        // Cập nhật AI của minion
                        minion.ai[0] = 1; // Chuyển sang trạng thái tấn công
                        minion.ai[1] = target.whoAmI; // Target player
                        
                        // Điều chỉnh hướng di chuyển
                        Vector2 direction = target.Center - minion.Center;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        
                        // Tốc độ di chuyển khác nhau cho từng loại minion
                        float speed = 8f;
                        if (minion.type == NPCID.Probe) speed = 10f;
                        if (minion.type == NPCID.ServantofCthulhu) speed = 12f;
                        
                        // Cập nhật vận tốc
                        minion.velocity = direction * speed;
                        
                        // Đồng bộ với client
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);

                        // Hiệu ứng điều khiển
                        if (Main.rand.Next(30) == 0)
                        {
                            int dustType = DustID.PurpleTorch;
                            if (minion.type == NPCID.Probe) dustType = DustID.Electric;
                            if (minion.type == NPCID.ServantofCthulhu) dustType = DustID.Blood;
                            
                            Dust.NewDust(minion.position, minion.width, minion.height, dustType);
                        }
                    }
                }
            }
        }

        private List<int> GetBossList()
        {
            return new List<int>
            {
                // Pre-hardmode
                NPCID.KingSlime,
                NPCID.EyeofCthulhu,
                NPCID.EaterofWorldsBody,
                NPCID.BrainofCthulhu,
                NPCID.QueenBee,
                NPCID.SkeletronHead,
                NPCID.WallofFlesh,
                NPCID.Deerclops,
                NPCID.QueenSlimeBoss, // Thêm Queen Slime

                // Hardmode
                NPCID.TheDestroyer,
                NPCID.Spazmatism,
                NPCID.Retinazer,
                NPCID.SkeletronPrime,
                NPCID.Plantera,
                NPCID.Golem,
                NPCID.DukeFishron,
                NPCID.HallowBoss,    // Thêm Empress of Light
                NPCID.CultistBoss,
                NPCID.MoonLordHead,

                // Event Bosses
                NPCID.DD2DarkMageT1,
                NPCID.DD2OgreT2,
                NPCID.DD2Betsy,
                NPCID.PirateShip,
                NPCID.MourningWood,
                NPCID.Pumpking,
                NPCID.IceQueen,
                NPCID.SantaNK1,
                NPCID.Everscream,
                NPCID.MartianSaucer
            };
        }

        private void GenerateBossNickname(NPC boss, int[] selectedSkills)
        {
            try
            {
                string originalName = Lang.GetNPCNameValue(boss.type);
                var skillNames = selectedSkills.Select(type => Lang.GetNPCNameValue(type)).ToList();
                
                // Tạo prefix ngẫu nhiên
                string[] prefixes = {
                    "Mutant", "Evolved", "Hybrid", "Corrupted", "Ascended",
                    "Legendary", "Divine", "Immortal", "Supreme", "Ancient",
                    "Chaotic", "Mystic", "Cosmic", "Primal", "Twisted"
                };
                
                // Tạo suffix dựa vào skills
                string[] suffixes = {
                    $"of {skillNames[0]}",
                    $"with Power of {skillNames[1]}",
                    $"Fusion with {skillNames[2]}",
                    $"Inheritor of {skillNames[3]}",
                    $"Combined with {skillNames[0]}",
                    $"Wielding {skillNames[1]}'s Might",
                    $"Infused by {skillNames[2]}'s Essence",
                    $"Enhanced by {skillNames[3]}'s DNA"
                };

                string nickname = $"{prefixes[random.Next(prefixes.Length)]} {originalName} {suffixes[random.Next(suffixes.Length)]}";
                
                if (bossNicknames.ContainsKey(boss.whoAmI))
                {
                    bossNicknames[boss.whoAmI] = nickname;
                }
                else
                {
                    bossNicknames.Add(boss.whoAmI, nickname);
                }
            }
            catch (Exception)
            {
                // Log lỗi nếu cần
            }
        }

        private void InitializeBossSkills()
        {
            void AddBossSkill(int bossType, string skillName, int difficulty,
                Func<NPC, bool> detector, Action<NPC, Player> executor)
            {
                if (!bossSkillPatterns.ContainsKey(bossType))
                {
                    bossSkillPatterns[bossType] = new List<BossSkillPattern>();
                }
                bossSkillPatterns[bossType].Add(new BossSkillPattern
                {
                    BossType = bossType,
                    SkillName = skillName,
                    Difficulty = difficulty,
                    DetectSkill = detector,
                    ExecuteSkill = executor
                });
            }

            // Ví dụ cho King Slime
            AddBossSkill(NPCID.KingSlime, "Royal Teleport", 2,
                (npc) => Main.rand.Next(120) == 0, // 1/120 chance mỗi frame
                (npc, target) => {
                    npc.position = target.Center + new Vector2(0, -200);
                    for (int i = 0; i < 50; i++) {
                        Dust.NewDust(npc.position, npc.width, npc.height, DustID.BlueTorch);
                    }
                });

            // Ví dụ cho Eye of Cthulhu  
            AddBossSkill(NPCID.EyeofCthulhu, "Dash Strike", 3,
                (npc) => Main.rand.Next(120) == 0, // Tăng tần suất từ 180 lên 120
                (npc, target) => {
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    npc.velocity = direction * 20f; // Tăng tốc độ từ 15f lên 20f
                    
                    // Thêm hiệu ứng dust khi dash
                    for (int i = 0; i < 20; i++) {
                        Dust.NewDust(npc.position, npc.width, npc.height, DustID.Blood);
                    }
                });

            // Brain of Cthulhu
            AddBossSkill(NPCID.BrainofCthulhu, "Clone Army", 4,
                (npc) => Main.rand.Next(300) == 0,
                (npc, target) => {
                    for (int i = 0; i < 4; i++) {
                        Vector2 position = target.Center + new Vector2(Main.rand.Next(-200, 200), Main.rand.Next(-200, 200));
                        int npcId = NPC.NewNPC(null, (int)position.X, (int)position.Y, NPCID.Creeper, 0);
                        
                        // Đảm bảo minion thuộc về boss
                        Main.npc[npcId].realLife = npc.whoAmI;
                        Main.npc[npcId].defense = npc.defense / 2; // Giảm defense của minion
                        
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", npcId);
                    }
                });

            // Queen Bee
            AddBossSkill(NPCID.QueenBee, "Stinger Storm", 3,
                (npc) => Main.rand.Next(240) == 0,
                (npc, target) => {
                    for (int i = 0; i < 8; i++) {
                        Vector2 direction = target.Center - npc.Center;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        
                        float spread = (float)(Main.rand.NextDouble() * 0.6 - 0.3);
                        direction = direction.RotatedBy(spread);
                        
                        Vector2 velocity = direction * 12f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.Stinger, 20, 2f, Main.myPlayer);
                        
                        // Đảm bảo projectile không gây sát thương cho boss
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        Main.projectile[projId].owner = Main.myPlayer;
                        
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Skeletron
            AddBossSkill(NPCID.SkeletronHead, "Bone Throw", 3,
                (npc) => Main.rand.Next(180) == 0,
                (npc, target) => {
                    for (int i = 0; i < 5; i++) {
                        Vector2 velocity = new Vector2(Main.rand.Next(-10, 11), Main.rand.Next(-10, 11));
                        velocity = velocity.SafeNormalize(Vector2.Zero) * 15f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.BoneGloveProj, 25, 2f, Main.myPlayer);
                        
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        Main.projectile[projId].owner = Main.myPlayer;
                        
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Wall of Flesh
            AddBossSkill(NPCID.WallofFlesh, "Laser Barrage", 4,
                (npc) => Main.rand.Next(210) == 0,
                (npc, target) => {
                    for (int i = 0; i < 3; i++) {
                        Vector2 position = npc.Center + new Vector2(0, Main.rand.Next(-200, 201));
                        // Tính hướng từ vị trí bắn đến người chơi
                        Vector2 direction = target.Center - position;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        Vector2 velocity = direction * 12f;
                        
                        int projId = Projectile.NewProjectile(null, position, velocity, ProjectileID.EyeLaser, 30, 2f, Main.myPlayer);
                        
                        // Đảm bảo projectile không gây sát thương cho boss
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        Main.projectile[projId].owner = Main.myPlayer;
                        
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // The Destroyer
            AddBossSkill(NPCID.TheDestroyer, "Probe Spawn", 4,
                (npc) => Main.rand.Next(300) == 0,
                (npc, target) => {
                    for (int i = 0; i < 2; i++) {
                        int npcId = NPC.NewNPC(null, (int)npc.Center.X, (int)npc.Center.Y, NPCID.Probe, 0);
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", npcId);
                    }
                });

            // The Twins
            AddBossSkill(NPCID.Retinazer, "Death Laser", 5,
                (npc) => Main.rand.Next(240) == 0,
                (npc, target) => {
                    // Bắn nhiều tia laser
                    for (int i = 0; i < 3; i++) {
                        Vector2 direction = target.Center - npc.Center;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        
                        // Thêm độ lệch cho mỗi tia
                        float spread = (float)(Main.rand.NextDouble() * 0.4 - 0.2);
                        direction = direction.RotatedBy(spread);
                        
                        Vector2 velocity = direction * 15f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.DeathLaser, 35, 2f, Main.myPlayer);
                        
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        Main.projectile[projId].owner = Main.myPlayer;
                        
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Plantera
            AddBossSkill(NPCID.Plantera, "Seed Barrage", 4,
                (npc) => Main.rand.Next(180) == 0,
                (npc, target) => {
                    for (int i = 0; i < 8; i++) {
                        Vector2 velocity = new Vector2(Main.rand.Next(-10, 11), Main.rand.Next(-10, 11));
                        velocity = velocity.SafeNormalize(Vector2.Zero) * 14f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.SeedPlantera, 25, 2f, Main.myPlayer);
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Duke Fishron
            AddBossSkill(NPCID.DukeFishron, "Bubble Attack", 5,
                (npc) => Main.rand.Next(210) == 0,
                (npc, target) => {
                    for (int i = 0; i < 6; i++) {
                        Vector2 velocity = new Vector2(Main.rand.Next(-10, 11), Main.rand.Next(-10, 11));
                        velocity = velocity.SafeNormalize(Vector2.Zero) * 16f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.Bubble, 30, 2f, Main.myPlayer);
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Moon Lord
            AddBossSkill(NPCID.MoonLordHead, "Phantasmal Sphere", 5,
                (npc) => Main.rand.Next(300) == 0,
                (npc, target) => {
                    // Bắn nhiều sphere với độ lệch
                    for (int i = 0; i < 3; i++) {
                        Vector2 direction = target.Center - npc.Center;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        
                        // Thêm độ lệch ngẫu nhiên
                        float spread = (float)(Main.rand.NextDouble() * 0.3 - 0.15);
                        direction = direction.RotatedBy(spread);
                        
                        Vector2 velocity = direction * 10f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.PhantasmalSphere, 40, 2f, Main.myPlayer);
                        
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        Main.projectile[projId].owner = Main.myPlayer;
                        
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Thêm Multi-Dash cho King Slime
            AddBossSkill(NPCID.KingSlime, "Royal Dash", 3,
                (npc) => Main.rand.Next(150) == 0,
                (npc, target) => {
                    // Dash 3 lần liên tiếp
                    for (int i = 0; i < 3; i++) {
                        Vector2 direction = target.Center - npc.Center;
                        direction = direction.SafeNormalize(Vector2.Zero);
                        npc.velocity = direction * 18f;
                        
                        // Hiệu ứng slime khi dash
                        for (int d = 0; d < 15; d++) {
                            Dust.NewDust(npc.position, npc.width, npc.height, DustID.BlueTorch);
                        }
                        
                        // Ngắt 10 frame giữa mỗi lần dash
                        System.Threading.Thread.Sleep(10);
                    }
                });

            // Thêm Charge Dash cho Duke Fishron
            AddBossSkill(NPCID.DukeFishron, "Shark Charge", 4,
                (npc) => Main.rand.Next(160) == 0,
                (npc, target) => {
                    // Tính toán điểm đích ở phía trước người chơi
                    Vector2 targetPos = target.Center + target.velocity * 20f;
                    Vector2 direction = targetPos - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    npc.velocity = direction * 25f; // Tốc độ rất cao
                    
                    // Tạo bong bóng khi dash
                    for (int i = 0; i < 10; i++) {
                        int projId = Projectile.NewProjectile(null, npc.Center, Vector2.Zero, ProjectileID.Bubble, 20, 2f, Main.myPlayer);
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Thêm Teleport Dash cho Moon Lord
            AddBossSkill(NPCID.MoonLordHead, "Cosmic Dash", 5,
                (npc) => Main.rand.Next(180) == 0,
                (npc, target) => {
                    // Teleport phía sau người chơi
                    Vector2 behindPlayer = target.Center - Vector2.Normalize(target.velocity) * 200f;
                    npc.Center = behindPlayer;
                    
                    // Dash về phía người chơi
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    npc.velocity = direction * 22f;
                    
                    // Hiệu ứng không gian
                    for (int i = 0; i < 30; i++) {
                        Dust.NewDust(npc.position, npc.width, npc.height, DustID.PurpleTorch);
                    }
                });

            // Deerclops
            AddBossSkill(NPCID.Deerclops, "Ice Spike Storm", 4,
                (npc) => Main.rand.Next(200) == 0,
                (npc, target) => {
                    for (int i = 0; i < 5; i++) {
                        Vector2 position = target.Center + new Vector2(Main.rand.Next(-200, 201), -600);
                        Vector2 velocity = new Vector2(Main.rand.Next(-3, 4), 12f);
                        int projId = Projectile.NewProjectile(null, position, velocity, ProjectileID.IceSickle, 30, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Thêm skill khác cho Queen Slime
            AddBossSkill(NPCID.KingSlime, "Crystal Rain", 4,
                (npc) => Main.rand.Next(180) == 0,
                (npc, target) => {
                    for (int i = 0; i < 8; i++) {
                        Vector2 velocity = new Vector2(Main.rand.Next(-10, 11), Main.rand.Next(-10, 11));
                        velocity = velocity.SafeNormalize(Vector2.Zero) * 14f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.CrystalShard, 25, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Dark Mage
            AddBossSkill(NPCID.DD2DarkMageT1, "Dark Energy", 3,
                (npc) => Main.rand.Next(200) == 0,
                (npc, target) => {
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    int projId = Projectile.NewProjectile(null, npc.Center, direction * 12f, ProjectileID.DD2DarkMageHeal, 25, 2f, Main.myPlayer);
                    Main.projectile[projId].hostile = true;
                    Main.projectile[projId].friendly = false;
                    TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                });

            // Betsy
            AddBossSkill(NPCID.DD2Betsy, "Dragon Breath", 5,
                (npc) => Main.rand.Next(180) == 0,
                (npc, target) => {
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    for (int i = -2; i <= 2; i++) {
                        Vector2 velocity = direction.RotatedBy(MathHelper.ToRadians(i * 15)) * 14f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.DD2BetsyFireball, 30, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Flying Dutchman
            AddBossSkill(NPCID.PirateShip, "Cannon Barrage", 4,
                (npc) => Main.rand.Next(210) == 0,
                (npc, target) => {
                    for (int i = 0; i < 5; i++) {
                        Vector2 position = npc.Center + new Vector2(Main.rand.Next(-100, 101), 0);
                        Vector2 velocity = new Vector2(0, 10f);
                        int projId = Projectile.NewProjectile(null, position, velocity, ProjectileID.CannonballHostile, 30, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Pumpking
            AddBossSkill(NPCID.Pumpking, "Flaming Scythe", 4,
                (npc) => Main.rand.Next(190) == 0,
                (npc, target) => {
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    int projId = Projectile.NewProjectile(null, npc.Center, direction * 16f, ProjectileID.FlamingScythe, 35, 2f, Main.myPlayer);
                    Main.projectile[projId].hostile = true;
                    Main.projectile[projId].friendly = false;
                    TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                });

            // Ice Queen
            AddBossSkill(NPCID.IceQueen, "Frost Wave", 5,
                (npc) => Main.rand.Next(200) == 0,
                (npc, target) => {
                    for (int i = -2; i <= 2; i++) {
                        Vector2 velocity = new Vector2(8f * i, -10f);
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.IceSpike, 30, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });

            // Martian Saucer
            AddBossSkill(NPCID.MartianSaucer, "Death Ray", 5,
                (npc) => Main.rand.Next(240) == 0,
                (npc, target) => {
                    Vector2 direction = target.Center - npc.Center;
                    direction = direction.SafeNormalize(Vector2.Zero);
                    int projId = Projectile.NewProjectile(null, npc.Center, direction * 20f, ProjectileID.MartianTurretBolt, 40, 2f, Main.myPlayer);
                    Main.projectile[projId].hostile = true;
                    Main.projectile[projId].friendly = false;
                    TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                });

            // Empress of Light
            AddBossSkill(NPCID.HallowBoss, "Prismatic Bolts", 5,
                (npc) => Main.rand.Next(160) == 0,
                (npc, target) => {
                    for (int i = 0; i < 8; i++) {
                        float rotation = MathHelper.TwoPi * i / 8f;
                        Vector2 velocity = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation)) * 16f;
                        int projId = Projectile.NewProjectile(null, npc.Center, velocity, ProjectileID.HallowBossRainbowStreak, 35, 2f, Main.myPlayer);
                        Main.projectile[projId].hostile = true;
                        Main.projectile[projId].friendly = false;
                        TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", projId);
                    }
                });
        }

        // Thêm hàm kiểm tra boss hardmode
        private bool IsHardmodeBoss(int bossType)
        {
            return bossType == NPCID.TheDestroyer ||
                   bossType == NPCID.Spazmatism ||
                   bossType == NPCID.Retinazer ||
                   bossType == NPCID.SkeletronPrime ||
                   bossType == NPCID.Plantera ||
                   bossType == NPCID.Golem ||
                   bossType == NPCID.DukeFishron ||
                   bossType == NPCID.CultistBoss ||
                   bossType == NPCID.MoonLordHead;
        }

        // Thêm hàm kiểm tra minion
        private bool IsMinion(int npcType)
        {
            return npcType == NPCID.Creeper ||
                   npcType == NPCID.Probe ||
                   npcType == NPCID.Bee ||
                   npcType == NPCID.GolemFistLeft ||
                   npcType == NPCID.GolemFistRight ||
                   npcType == NPCID.Sharkron ||
                   npcType == NPCID.Sharkron2 ||
                   npcType == NPCID.ServantofCthulhu ||
                   npcType == NPCID.EaterofSouls ||
                   npcType == NPCID.VileSpitEaterOfWorlds ||
                   npcType == NPCID.GolemHead ||
                   npcType == NPCID.CultistDragonHead ||
                   npcType == NPCID.BeeSmall ||
                   npcType == NPCID.MoonLordFreeEye ||
                   npcType == NPCID.MoonLordHead ||
                   npcType == NPCID.SkeletronHand ||
                   // Thêm các minion khác
                   npcType == NPCID.TheDestroyerBody ||
                   npcType == NPCID.PlanterasTentacle ||
                   npcType == NPCID.QueenSlimeMinionBlue ||
                   npcType == NPCID.QueenSlimeMinionPink ||
                   npcType == NPCID.SkeletronPrime ||
                   npcType == NPCID.PrimeCannon ||
                   npcType == NPCID.PrimeLaser ||
                   npcType == NPCID.PrimeSaw ||
                   npcType == NPCID.PrimeVice;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NpcSpawn.Deregister(this, OnNPCSpawn);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnNPCAI);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNPCKilled);
                bossNicknames.Clear();
                currentSkills.Clear();
                skillCooldowns.Clear();
                customAI.Clear();
                bossPhases.Clear();
            }
            base.Dispose(disposing);
        }
    }

    public class BossSkillPattern
    {
        public int BossType { get; set; }
        public string SkillName { get; set; }
        public Func<NPC, bool> DetectSkill { get; set; }
        public Action<NPC, Player> ExecuteSkill { get; set; }
        public int Difficulty { get; set; } // 1-5: 1 là dễ nhất, 5 là khó nhất
    }

    public class BossPhase
    {
        public float HealthPercentage { get; set; }
        public List<BossSkillPattern> PhaseSkills { get; set; }
    }
} 