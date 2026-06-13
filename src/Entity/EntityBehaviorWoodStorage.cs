using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// A slot that only accepts WOOD and wood-related items — logs, planks, sticks,
    /// firewood, debarked logs, branchy/resin logs, saplings, lumber, and modded wood.
    ///
    /// Unlike fuel (which has a clean CombustibleProps flag), wood has no single property
    /// to test, so this matches on the collectible's CODE. We look for wood-ish tokens in
    /// the code path AND honour an explicit opt-in/opt-out so authors and other mods can
    /// tune it without editing this file:
    ///   - An item is wood if its code path contains any token in WoodTokens, OR its
    ///     collectible Attributes has "isWood": true.
    ///   - An item is NEVER wood if its Attributes has "isWood": false (hard override),
    ///     or its path contains a token in NotWoodTokens (avoids false positives like
    ///     "redwood-sapling" being fine but "wooden-bowl"/"wood-bucket" tools you may not
    ///     want — tune NotWoodTokens to taste).
    /// This keeps it mod-agnostic and easy to adjust.
    /// </summary>
    public class ItemSlotWoodOnly : ItemSlot
    {
        // Code-path substrings that mark an item as wood-related. Lowercase; matched as
        // substrings of the FULL code path (domain stripped). Add tokens as needed.
        private static readonly string[] WoodTokens = new[]
        {
            "log", "plank", "planks", "stick", "firewood", "lumber", "board",
            "sapling", "driftwood", "debarked", "branchy", "logsection", "woodchips",
            "treetrunk", "timber"
        };

        // Code-path substrings that EXCLUDE an item even if a wood token also matched.
        // Tune to taste — e.g. keep crafted wooden tools/containers out of a log car.
        private static readonly string[] NotWoodTokens = new[]
        {
            "bowl", "bucket", "tool", "axe", "shovel", "pickaxe", "hoe", "scythe",
            "door", "chest", "barrel", "sign", "ladder", "fence", "support"
        };

        public ItemSlotWoodOnly(InventoryBase inventory) : base(inventory) { }

        public static bool IsWood(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) return false;

            // Explicit override via collectible attributes wins over code matching.
            var attrs = stack.Collectible.Attributes;
            if (attrs != null && attrs["isWood"].Exists)
            {
                return attrs["isWood"].AsBool(false);
            }

            string path = stack.Collectible.Code.Path?.ToLowerInvariant();
            if (string.IsNullOrEmpty(path)) return false;

            foreach (var bad in NotWoodTokens)
                if (path.Contains(bad)) return false;

            foreach (var tok in WoodTokens)
                if (path.Contains(tok)) return true;

            return false;
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && IsWood(sourceSlot?.Itemstack);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && IsWood(sourceSlot?.Itemstack);
        }
    }

    /// <summary>
    /// Gives a train entity (e.g. a log car) a WOOD-ONLY storage inventory with a GUI.
    /// Right-click the car with an empty hand to open it.
    ///
    /// Inventory: an InventoryGeneric whose slots are ItemSlotWoodOnly, persisted in the
    /// entity's WatchedAttributes tree ("vrrwoodinv") so cargo survives save/reload.
    ///
    /// GUI: opened via the vanilla GuiDialogBlockEntityInventory using the verified ctor
    ///   (string title, InventoryBase inv, BlockPos pos, int cols, ICoreClientAPI capi).
    /// The inventory is Open()'d for the player on both sides so slot moves sync through
    /// the engine's standard inventory networking (no custom packet plumbing).
    ///
    /// JSON: add to the entity's client AND server behaviors as
    ///   { "code": "woodstorage", "quantitySlots": 16 }
    /// </summary>
    public class EntityBehaviorWoodStorage : EntityBehavior
    {
        private InventoryGeneric _inv;
        private GuiDialog _dialog;
        private int _slots = 16;

        public EntityBehaviorWoodStorage(Entity entity) : base(entity) { }

        public InventoryGeneric Inventory => _inv;
        public override string PropertyName() => "woodstorage";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            _slots = attributes?["quantitySlots"].AsInt(16) ?? 16;

            _inv = new InventoryGeneric(_slots, "vrrwood-" + entity.EntityId, entity.Api,
                (id, inv) => new ItemSlotWoodOnly(inv));

            var tree = entity.WatchedAttributes.GetTreeAttribute("vrrwoodinv");
            if (tree != null) _inv.FromTreeAttributes(tree);
            _inv.ResolveBlocksOrItems();

            _inv.SlotModified += OnSlotModified;
        }

        private void OnSlotModified(int slotId)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;
            var tree = new TreeAttribute();
            _inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["vrrwoodinv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("vrrwoodinv");
        }

        /// <summary>Open the wood storage for a player (called from EntityTrain.OnInteract).
        /// Returns true if handled.</summary>
        public bool OpenFor(IPlayer player)
        {
            if (player == null || _inv == null) return false;

            // Server: register the player as having the inventory open so slot edits sync.
            if (entity.Api.Side == EnumAppSide.Server)
            {
                _inv.Open(player);
                return true;
            }

            // Client: open (or toggle) the dialog. Bind to the block position under the
            // entity — GuiDialogBlockEntityInventory needs a BlockPos for sync routing.
            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return false;

            if (_dialog != null && _dialog.IsOpened()) { _dialog.TryClose(); return true; }

            _inv.Open(player);
            BlockPos pos = entity.ServerPos.AsBlockPos;
            _dialog = new GuiDialogBlockEntityInventory("Wood Storage", _inv, pos, 8, capi);
            _dialog.OnClosed += () => { _inv.Close(player); };
            _dialog.TryOpen();
            return true;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (_dialog != null && _dialog.IsOpened()) _dialog.TryClose();
            base.OnEntityDespawn(despawn);
        }

        /// <summary>Drop all stored wood into the world (call when the car is removed so
        /// the cargo isn't lost). Server-side.</summary>
        public void DropContents()
        {
            if (entity.Api.Side != EnumAppSide.Server || _inv == null) return;
            _inv.DropAll(entity.ServerPos.XYZ);
        }
    }
}
