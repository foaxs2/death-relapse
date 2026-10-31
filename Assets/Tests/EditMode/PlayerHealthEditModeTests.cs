using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerHealthEditModeTests
{
    private GameObject playerGo;
    private PlayerHealth playerHealth;
    private PlayerDodge playerDodge;

    [SetUp]
    public void SetUp()
    {
        playerGo = new GameObject("TestPlayer");
        // Các RequireComponent: CharacterController và PlayerMovement sẽ tự được thêm
        playerDodge = playerGo.AddComponent<PlayerDodge>();
        playerHealth = playerGo.AddComponent<PlayerHealth>();

        // Gọi Awake qua Reflection để mô phỏng vòng đời Unity trong EditMode
        MethodInfo awakeMethod = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        awakeMethod?.Invoke(playerHealth, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (playerGo != null)
        {
            Object.DestroyImmediate(playerGo);
        }
    }

    [Test]
    public void TakeDamage_From300HP_ReducesTo225()
    {
        // 1. Kiểm tra máu ban đầu 300 HP
        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Máu khởi tạo phải bằng 300 HP");

        // Nhận sát thương 75 HP
        playerHealth.TakeDamage(75f);

        // Kiểm tra máu còn đúng 225 HP
        Assert.AreEqual(225f, playerHealth.CurrentHealth, "Máu sau khi nhận 75 damage phải còn lại 225 HP");
        Assert.IsFalse(playerHealth.IsDead, "Nhân vật phải còn sống");
    }

    [Test]
    public void ReceiveBossAttackHit_WhenPlayerDodgeIsInvulnerable_DoesNotLoseHealth()
    {
        // Tạo ScriptableObject BossAttackData giả lập với damage 75
        BossAttackData attackData = ScriptableObject.CreateInstance<BossAttackData>();
        FieldInfo damageField = typeof(BossAttackData).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic);
        damageField?.SetValue(attackData, 75f);

        // Giả lập trạng thái bất tử (IsInvulnerable = true) trên PlayerDodge bằng reflection
        FieldInfo invulnerableField = typeof(PlayerDodge).GetField("isInvulnerable", BindingFlags.Instance | BindingFlags.NonPublic);
        invulnerableField?.SetValue(playerDodge, true);

        Assert.IsTrue(playerDodge.IsInvulnerable, "PlayerDodge phải ở trạng thái IsInvulnerable = true");

        // Gọi hàm nhận đòn từ Boss
        playerHealth.ReceiveBossAttackHit(attackData, true);

        // Kiểm tra máu vẫn còn nguyên 300 HP
        Assert.AreEqual(300f, playerHealth.CurrentHealth, "Khi có I-frame, máu không được giảm");

        Object.DestroyImmediate(attackData);
    }

    [Test]
    public void TakeDamage_WhenHealthReachesZero_CallsDeath()
    {
        bool onDiedCalled = false;
        playerHealth.OnDied += () => onDiedCalled = true;

        // Gây sát thương 300 HP (đủ làm máu về 0)
        playerHealth.TakeDamage(300f);

        Assert.AreEqual(0f, playerHealth.CurrentHealth, "Máu nhân vật phải bằng 0");
        Assert.IsTrue(playerHealth.IsDead, "IsDead phải chuyển thành true");
        Assert.IsTrue(onDiedCalled, "Sự kiện OnDied phải được kích hoạt");
    }
}

