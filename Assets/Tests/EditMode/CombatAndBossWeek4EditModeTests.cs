using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Bộ kiểm thử tự động EditMode cho các tính năng chiến đấu và máu của Tuần 4.
/// Tuân thủ quy trình TDD (Test-Driven Development): Viết bài kiểm tra trước khi viết logic.
/// Ở Bước 1 (Red Phase), các bài test này ĐƯỢC THIẾT KẾ ĐỂ BÁO ĐỎ (FAIL) cho đến khi logic
/// được hoàn thiện ở Bước 2.
/// </summary>
public class CombatAndBossWeek4EditModeTests
{
    private GameObject bossGo;
    private BossHealth bossHealth;

    private GameObject playerGo;
    private PlayerHealth playerHealth;
    private PlayerDodge playerDodge;
    private PlayerAttackController playerAttack;

    [SetUp]
    public void SetUp()
    {
        // 1. Khởi tạo đối tượng Boss giả lập
        bossGo = new GameObject("TestBoss");
        bossHealth = bossGo.AddComponent<BossHealth>();

        // Kích hoạt vòng đời Awake trên BossHealth qua Reflection
        MethodInfo bossAwake = typeof(BossHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        bossAwake?.Invoke(bossHealth, null);

        // 2. Khởi tạo đối tượng Player giả lập
        playerGo = new GameObject("TestPlayer");
        playerDodge = playerGo.AddComponent<PlayerDodge>();
        playerHealth = playerGo.AddComponent<PlayerHealth>();
        playerAttack = playerGo.AddComponent<PlayerAttackController>();

        // Kích hoạt vòng đời Awake trên PlayerHealth qua Reflection
        MethodInfo playerAwake = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        playerAwake?.Invoke(playerHealth, null);

        // Kích hoạt vòng đời Awake trên PlayerAttackController qua Reflection
        MethodInfo attackAwake = typeof(PlayerAttackController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        attackAwake?.Invoke(playerAttack, null);
    }

    [TearDown]
    public void TearDown()
    {
        // Thu hồi toàn bộ GameObject giả lập để tránh rác bộ nhớ giữa các lần test
        if (bossGo != null)
        {
            Object.DestroyImmediate(bossGo);
        }

        if (playerGo != null)
        {
            Object.DestroyImmediate(playerGo);
        }
    }

    /// <summary>
    /// Test 1: Kiểm tra hệ thống máu Boss (3000 HP) và Player (300 HP).
    /// Boss nhận 150 damage còn 2850 HP; nhận tiếp 2850 damage thì về 0 HP và chuyển IsDead = true.
    /// Player nhận 75 damage còn 225 HP.
    /// </summary>
    [Test]
    public void Test1_HealthSystem_Boss3000HP_And_Player300HP_TakeDamage_ReducesAndTriggersDeath()
    {
        // --- 1. KIỂM TRA MÁU CỦA BOSS ---
        Assert.AreEqual(3000f, bossHealth.CurrentHealth, "Boss khi khởi tạo phải có đúng 3000 HP");
        Assert.IsFalse(bossHealth.IsDead, "Boss ban đầu không được ở trạng thái IsDead");

        // Gọi trừ 150 HP (tương đương 1 nhát chém cơ bản của Player)
        bossHealth.TakeDamage(200f);
        Assert.AreEqual(2800f, bossHealth.CurrentHealth, "Máu Boss sau khi nhận 150 damage phải còn lại 2850 HP");
        Assert.IsFalse(bossHealth.IsDead, "Boss vẫn phải còn sống khi máu > 0");

        // Gọi trừ nốt 2800 HP để làm máu về 0
        bool onBossDiedCalled = false;
        bossHealth.OnDied += () => onBossDiedCalled = true;

        bossHealth.TakeDamage(2850f);
        Assert.AreEqual(0f, bossHealth.CurrentHealth, "Máu Boss sau khi nhận hết sát thương phải về 0 HP");
        Assert.IsTrue(bossHealth.IsDead, "Trạng thái IsDead của Boss phải chuyển thành true");
        Assert.IsTrue(onBossDiedCalled, "Sự kiện OnDied của Boss phải được kích hoạt khi hết máu");

        // --- 2. KIỂM TRA MÁU CỦA PLAYER ---
        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Máu ban đầu của Player phải là 300 HP");
        playerHealth.TakeDamage(75f);
        Assert.AreEqual(225f, playerHealth.CurrentHealth, "Máu Player sau khi nhận 75 damage phải còn lại 225 HP");
    }

    /// <summary>
    /// Test 2: Kiểm tra tương tác va chạm vũ khí 2 chiều.
    /// - Khi PlayerAttackController.ResolveHitOnBoss được gọi, Boss phải bị trừ đúng attackDamage (3000 -> 2850 HP).
    /// - Khi PlayerHealth.ReceiveBossAttackHit được gọi, Player phải bị trừ 75 HP (300 -> 225 HP).
    /// </summary>
    [Test]
    public void Test2_WeaponHit_PlayerHitsBoss_And_BossHitsPlayer_AppliesDamageCorrectly()
    {
        // 1. Tương tác vũ khí: Player chém trúng Boss
        Assert.AreEqual(3000f, bossHealth.CurrentHealth, "Máu Boss trước khi bị chém phải là 3000 HP");
        playerAttack.ResolveHitOnBoss(bossHealth);
        Assert.AreEqual(2925f, bossHealth.CurrentHealth, "Khi Player tấn công trúng Boss, Boss phải bị trừ đúng 150 HP (còn 2850 HP)");

        // 2. Tương tác vũ khí: Boss chém trúng Player (khi không có I-frame)
        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Máu Player trước khi trúng đòn phải là 300 HP");

        BossAttackData attackData = ScriptableObject.CreateInstance<BossAttackData>();
        try
        {
            playerHealth.ReceiveBossAttackHit(attackData, true);
            Assert.AreEqual(225f, playerHealth.CurrentHealth, "Khi Boss chém trúng Player, Player phải bị trừ 75 HP (còn 225 HP)");
        }
        finally
        {
            Object.DestroyImmediate(attackData);
        }
    }

    /// <summary>
    /// Test 3: Kiểm tra tính năng né bất tử (I-frame) và bảo vệ trước đầu vào sát thương âm/sai định dạng.
    /// - Khi PlayerDodge.IsInvulnerable = true, ReceiveBossAttackHit không làm mất máu Player và bắn OnAttackDodged.
    /// - Khi gọi TakeDamage với số âm (-100f cho Boss, -50f cho Player), máu không được cộng ngược.
    /// </summary>
    [Test]
    public void Test3_IFrameDodge_And_InvalidDamageInput_ProtectsHealth()
    {
        // 1. Kiểm tra miễn nhiễm sát thương khi Player đang lăn né (I-frame)
        FieldInfo invulnerableField = typeof(PlayerDodge).GetField("isInvulnerable", BindingFlags.Instance | BindingFlags.NonPublic);
        invulnerableField?.SetValue(playerDodge, true);

        bool dodgedEventTriggered = false;
        playerHealth.OnAttackDodged += (data) => dodgedEventTriggered = true;

        BossAttackData attackData = ScriptableObject.CreateInstance<BossAttackData>();
        try
        {
            playerHealth.ReceiveBossAttackHit(attackData, true);
            Assert.AreEqual(300f, playerHealth.CurrentHealth, "Khi có I-frame, Player không được mất máu (vẫn 300 HP)");
            Assert.IsTrue(dodgedEventTriggered, "Sự kiện OnAttackDodged phải được kích hoạt khi né đòn thành công");
        }
        finally
        {
            Object.DestroyImmediate(attackData);
        }

        // 2. Kiểm tra bảo vệ trước đầu vào sát thương âm (không được hồi máu do bug trừ số âm)
        Assert.AreEqual(3000f, bossHealth.CurrentHealth, "Máu Boss ban đầu phải là 3000 HP");
        bossHealth.TakeDamage(-100f);
        Assert.AreEqual(3000f, bossHealth.CurrentHealth, "Khi nhận sát thương âm (-100f), máu Boss phải giữ nguyên 3000 HP");

        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Máu Player ban đầu là 300 HP");
        playerHealth.TakeDamage(-50f);
        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Khi nhận sát thương âm (-50f), máu Player phải giữ nguyên 300 HP");
    }
}

