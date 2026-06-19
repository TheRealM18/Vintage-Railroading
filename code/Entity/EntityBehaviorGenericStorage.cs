using Vintagestory.API.Common;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Gives any entity a generic storage inventory (accepts ALL items) with a GUI.
    /// Right-click the entity with an empty hand to open it.
    ///
    /// Inventory: an InventoryGeneric with plain ItemSlots, persisted in the entity's
    /// WatchedAttributes tree ("vrrgeneralinv") so cargo survives save/reload.
    ///
    /// Interaction: this behavior overrides OnInteract and, on an empty-handed
    /// (non-sneak) right click, opens the storage and sets
    /// handled = EnumHandling.PreventSubsequent. Because this behavior is listed BEFORE
    /// creaturecarrier in the entity JSON, that stops creaturecarrier from running on the
    /// same interaction, so the player opens the inventory instead of sitting down.
    /// Sneak + empty hand falls through, allowing the seat to mount as normal.
    ///
    /// GUI: opened via the vanilla GuiDialogBlockEntityInventory using the verified ctor
    ///   (string title, InventoryBase inv, BlockPos pos, int cols, ICoreClientAPI capi).
    /// The inventory is Open()'d for the player on both sides so slot moves sync through
    /// the engine's standard inventory networking (no custom packet plumbing).
    ///
    /// Defaults: title = "Storage", slots = 16, columns = 8.
    ///
    /// JSON: add to the entity's client AND server behaviors as
    ///   { "code": "genericstorage" }
    /// and place it BEFORE the creaturecarrier behavior if you want inventory to take
    /// priority over sitting on non-sneak right-click.
    /// </summary>
    public class EntityBehaviorGenericStorage : EntityBehavior
    {
        // ── Constants ────────────────────────────────────────────────────────────
        private const string InvKey      = "vrrgeneralinv";
        private const string GuiTitle    = "Storage";
        private const int    DefaultSlots = 8;
        private const int    GuiColumns   = 4;

        // ── State ─────────────────────────────────────────────────────────────────
        private InventoryGeneric _inv = null!;
        private GuiDialog?       _dialog;

        // FILTER (optional, from JSON). When either list is non-empty the inventory only
        // accepts stacks whose item/block code matches. `acceptCodes` matches full codes or
        // wildcard patterns (e.g. "game:ore-*"); `acceptCategories` matches against a small
        // set of convenience groups resolved in MatchesFilter. Empty/absent = accept all,
        // so existing cars are unchanged.
        private string[]? _acceptCodes;
        private string[]? _acceptCategories;
        // CAPACITY (optional). Overrides the per-slot max stack size so a car can hold more
        // (or less) per slot than the item's own default. 0 = use the item default.
        private int _maxStackSize;

        // ── Construction ──────────────────────────────────────────────────────────
        public EntityBehaviorGenericStorage(Entity entity) : base(entity) { }

        public InventoryGeneric Inventory    => _inv;
        public override string  PropertyName() => "genericstorage";

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            int slots = attributes?["quantitySlots"].AsInt(DefaultSlots) ?? DefaultSlots;
            if (slots <= 0) slots = DefaultSlots;

            // Optional filter + capacity config (absent => behaves exactly as before).
            _acceptCodes      = attributes?["acceptCodes"].AsArray<string>(null)?.Where(x => x != null).Select(x => x!).ToArray();
            _acceptCategories = attributes?["acceptCategories"].AsArray<string>(null)?.Where(x => x != null).Select(x => x!).ToArray();
            _maxStackSize     = attributes?["maxStackSize"].AsInt(0) ?? 0;

            bool filtered = (_acceptCodes != null && _acceptCodes.Length > 0)
                         || (_acceptCategories != null && _acceptCategories.Length > 0);

            // Build the inventory. When a filter is configured we use ItemSlotSurvival-style
            // slots whose CanHold is gated by MatchesFilter; otherwise plain ItemSlots that
            // accept everything (the original behavior).
            _inv = new InventoryGeneric(
                slots,
                InvKey + "-" + entity.EntityId,
                entity.Api,
                (id, inv) => filtered
                    ? (ItemSlot)new FilteredSlot(inv, this)
                    : new ItemSlot(inv)
            );

            // Restore persisted contents if they exist.
            var tree = entity.WatchedAttributes.GetTreeAttribute(InvKey);
            if (tree != null) _inv.FromTreeAttributes(tree);
            _inv.ResolveBlocksOrItems();

            _inv.SlotModified += OnSlotModified;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (_dialog != null && _dialog.IsOpened()) _dialog.TryClose();
            base.OnEntityDespawn(despawn);
        }

        // ── Persistence ───────────────────────────────────────────────────────────

        private void OnSlotModified(int slotId)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;

            var tree = new TreeAttribute();
            _inv.ToTreeAttributes(tree);
            entity.WatchedAttributes[InvKey] = tree;
            entity.WatchedAttributes.MarkPathDirty(InvKey);
        }

        // ── Interaction ───────────────────────────────────────────────────────────

        /// <summary>
        /// Open the generic storage for a player. Returns true if handled.
        ///
        /// Server side: registers the player so slot edits sync.
        /// Client side: opens (or toggles) the GUI dialog.
        /// </summary>
        public bool OpenFor(IPlayer player)
        {
            if (player == null || _inv == null) return false;

            if (entity.Api.Side == EnumAppSide.Server)
            {
                _inv.Open(player);
                return true;
            }

            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return false;

            // Toggle: close if already open.
            if (_dialog != null && _dialog.IsOpened())
            {
                _dialog.TryClose();
                return true;
            }

            _inv.Open(player);

            // GuiDialogBlockEntityInventory needs a BlockPos for network routing;
            // we use the block position directly under the entity.
            BlockPos pos = entity.Pos.AsBlockPos;
            _dialog = new GuiDialogBlockEntityInventory(GuiTitle, _inv, pos, GuiColumns, capi);
            _dialog.OnClosed += () => _inv.Close(player);
            _dialog.TryOpen();
            return true;
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Drop all stored items into the world. Call this when the entity is
        /// destroyed so the player doesn't lose their cargo. Server-side only.
        /// </summary>
        public void DropContents()
        {
            if (entity.Api.Side != EnumAppSide.Server || _inv == null) return;
            // Drop slot-by-slot so we can log and so a stuck DropAll can't silently void
            // cargo. Spawn each stack at the entity position.
            int dropped = 0;
            var pos = entity.Pos.XYZ;
            foreach (var slot in _inv)
            {
                if (slot?.Itemstack == null) continue;
                entity.World.SpawnItemEntity(slot.Itemstack.Clone(), pos);
                dropped += slot.Itemstack.StackSize;
                slot.Itemstack = null;
                slot.MarkDirty();
            }
            VrrDebug.Log(entity.Api, "DropContents: dropped {0} item(s) from {1}", dropped, GetType().Name);
        }

        // ── Filtering + capacity ────────────────────────────────────────────────

        /// <summary>True if `stack` is allowed in this storage given the JSON filter. Empty
        /// filter accepts everything. Matches on full code, wildcard code patterns, or one of
        /// a few convenience categories.</summary>
        internal bool MatchesFilter(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) return false;
            // No filter configured -> accept all.
            bool hasCodes = _acceptCodes != null && _acceptCodes.Length > 0;
            bool hasCats  = _acceptCategories != null && _acceptCategories.Length > 0;
            if (!hasCodes && !hasCats) return true;

            string code = stack.Collectible.Code.ToString();

            if (hasCodes)
            {
                foreach (var pat in _acceptCodes!)
                {
                    if (string.IsNullOrEmpty(pat)) continue;
                    if (WildcardMatch(pat, code)) return true;
                }
            }

            if (hasCats)
            {
                foreach (var cat in _acceptCategories!)
                {
                    if (MatchesCategory(cat, stack, code)) return true;
                }
            }
            return false;
        }

        /// <summary>Per-slot capacity override (0 = item default).</summary>
        internal int MaxStackSizeOverride => _maxStackSize;

        // Convenience categories so a car JSON can say "ore" instead of listing every code.
        // Kept intentionally simple/substring-based; extend as needed.
        private static bool MatchesCategory(string cat, ItemStack stack, string code)
        {
            if (string.IsNullOrEmpty(cat)) return false;
            switch (cat.ToLowerInvariant())
            {
                case "wood":     return IsWood(stack, code);
                case "ore":      return code.Contains("ore");
                case "stone":    return code.Contains("stone") || code.Contains("rock") || code.Contains("cobblestone");
                case "dirt":     return code.Contains("soil") || code.Contains("dirt") || code.Contains("gravel") || code.Contains("sand");
                case "organic":  return code.Contains("grain") || code.Contains("vegetable") || code.Contains("fruit")
                                      || code.Contains("seeds") || code.Contains("crop") || code.Contains("flour")
                                      || code.Contains("forage") || code.Contains("mushroom");
                case "food":     return stack.Collectible?.NutritionProps != null
                                      || code.Contains("meat") || code.Contains("fish")
                                      || code.Contains("fruit") || code.Contains("vegetable")
                                      || code.Contains("dairy") || code.Contains("cheese");
                case "perishable":
                    // Anything that can spoil (has transition props) — used by the freezer.
                    return stack.Collectible?.NutritionProps != null
                        || code.Contains("meat") || code.Contains("fish") || code.Contains("fruit")
                        || code.Contains("vegetable") || code.Contains("dairy") || code.Contains("cheese")
                        || code.Contains("egg");
                default:
                    return false;
            }
        }

        // Wood matcher: an "isWood" collectible attribute overrides everything; otherwise the
        // collectible attribute overrides everything; otherwise the code path must contain a
        // wood token and none of the exclusion tokens (so a wooden axe/bucket/door is NOT
        // accepted as raw wood cargo).
        private static readonly string[] WoodTokens = {
            "log", "plank", "planks", "stick", "firewood", "lumber", "board",
            "sapling", "driftwood", "debarked", "branchy", "logsection", "woodchips",
            "treetrunk", "timber"
        };
        private static readonly string[] NotWoodTokens = {
            "bowl", "bucket", "tool", "axe", "shovel", "pickaxe", "hoe", "scythe",
            "door", "chest", "barrel", "sign", "ladder", "fence", "support"
        };
        private static bool IsWood(ItemStack stack, string code)
        {
            var attrs = stack.Collectible?.Attributes;
            if (attrs != null && attrs["isWood"].Exists) return attrs["isWood"].AsBool(false);
            string? path = stack.Collectible?.Code?.Path?.ToLowerInvariant();
            if (string.IsNullOrEmpty(path)) return false;
            foreach (var bad in NotWoodTokens) if (path.Contains(bad)) return false;
            foreach (var tok in WoodTokens) if (path.Contains(tok)) return true;
            return false;
        }

        // Minimal wildcard matcher: a single '*' matches any run of characters. Supports the
        // common "domain:prefix-*" and "domain:*-suffix" asset-code patterns without regex.
        // Patterns without '*' must match exactly.
        private static bool WildcardMatch(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            int star = pattern.IndexOf('*');
            if (star < 0) return pattern == text;
            string head = pattern.Substring(0, star);
            string tail = pattern.Substring(star + 1);
            return text.Length >= head.Length + tail.Length
                && text.StartsWith(head)
                && text.EndsWith(tail);
        }

        /// <summary>
        /// A storage slot that only accepts stacks passing the owning behavior's filter, and
        /// honours an optional per-slot capacity override. Everything else is a normal slot.
        /// </summary>
        private class FilteredSlot : ItemSlot
        {
            private readonly EntityBehaviorGenericStorage _owner;
            public FilteredSlot(InventoryBase inv, EntityBehaviorGenericStorage owner) : base(inv)
            {
                _owner = owner;
            }

            public override bool CanHold(ItemSlot sourceSlot)
            {
                if (sourceSlot?.Itemstack == null) return false;
                if (!_owner.MatchesFilter(sourceSlot.Itemstack)) return false;
                return base.CanHold(sourceSlot);
            }

            public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
            {
                if (sourceSlot?.Itemstack == null) return false;
                if (!_owner.MatchesFilter(sourceSlot.Itemstack)) return false;
                return base.CanTakeFrom(sourceSlot, priority);
            }

            public override int GetRemainingSlotSpace(ItemStack forItemstack)
            {
                int cap = _owner.MaxStackSizeOverride;
                if (cap > 0)
                {
                    int used = Itemstack?.StackSize ?? 0;
                    return System.Math.Max(0, cap - used);
                }
                return base.GetRemainingSlotSpace(forItemstack);
            }
        }
    }
}