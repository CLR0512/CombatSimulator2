namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>What blocking costs with a master, read from the setup when its run started.</summary>
public sealed class MasterFacts
{
    /// <summary>What one block costs, the setup's cost modifiers included.</summary>
    public long BlockCost { get; set; }

    /// <summary>The currency blocks cost: Slayer Coins.</summary>
    public string Currency { get; set; } = "";

    /// <summary>The master's block slots in order: open in the save or not, and the tasks still needed to open one.</summary>
    public List<BlockSlotFacts> Slots { get; set; } = [];

    /// <summary>How many slots the save has open.</summary>
    public int OpenSlots => Slots.Count(s => s.Open);
}

/// <summary>One block slot of a master.</summary>
public sealed class BlockSlotFacts
{
    public string Id { get; set; } = "";
    public bool Open { get; set; }

    /// <summary>Tasks with the master still needed before the slot opens; 0 when open or when unknown.</summary>
    public int TasksNeeded { get; set; }
}
