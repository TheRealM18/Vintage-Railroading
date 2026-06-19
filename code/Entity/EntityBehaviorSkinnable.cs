using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Cosmetic SKIN behavior for rolling stock. A car/loco can define any number of skins
    /// in its entity JSON; the player cycles between them on the placed entity and the choice
    /// persists. The model/shape never changes — only the texture set — so a skin is a pure
    /// reskin.
    ///
    /// JSON (entity attributes):
    ///   "skins": [
    ///     { "name": "steel", "textures": { "red": { "base": "game:block/metal/plate/iron" } } },
    ///     { "name": "rust",  "textures": { "red": { "base": "game:block/metal/plate/rusty" } } },
    ///     ...
    ///   ]
    /// Skin 0 is the default. Each skin's "textures" map is layered ON TOP of the entity's
    /// base client.textures, so a skin only needs to list the keys it overrides (e.g. just
    /// "red"); everything else falls back to the base look. The number of skins is read from
    /// the array, so adding more is a DATA-ONLY change — no recompile.
    ///
    /// Selection: the active index lives in WatchedAttributes ("vrrSkin"). Call
    /// <see cref="CycleSkin"/> (server-side) to advance to the next skin; the entity re-skins
    /// and the change syncs to all clients. The owning entity decides the trigger (e.g.
    /// sneak + right-click) and forwards to CycleSkin.
    /// </summary>
    public class EntityBehaviorSkinnable : EntityBehavior
    {
        private List<SkinDef> _skins = new List<SkinDef>();
        // Set once we've replaced this entity's texture dict with a private copy, so skin
        // changes don't mutate the shared entity-type Properties.
        private bool _ownTextures;

        public EntityBehaviorSkinnable(Entity entity) : base(entity) { }

        public override string PropertyName() => "skinnable";

        /// <summary>Active skin index, persisted + synced.</summary>
        public int SkinIndex
        {
            get => entity.WatchedAttributes.GetInt("vrrSkin", 0);
            set => entity.WatchedAttributes.SetInt("vrrSkin", value);
        }

        public int SkinCount => _skins.Count;

        public string ActiveSkinName =>
            (_skins.Count > 0 && SkinIndex >= 0 && SkinIndex < _skins.Count)
                ? _skins[SkinIndex].Name : "default";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            ParseSkins(attributes);

            // Apply the persisted skin on load ONLY if it's a non-default index. Skin 0 is the
            // base look, which the entity's own client.textures already shows, so there's
            // nothing to bake at load for the common case — avoiding any atlas work before the
            // client is fully ready. A non-zero saved skin re-applies here.
            if (SkinIndex > 0) ApplySkin(SkinIndex, redraw: false);

            // Re-apply (and redraw) whenever the synced index changes, e.g. another player
            // cycled it, or it arrived from the server after spawn.
            entity.WatchedAttributes.RegisterModifiedListener("vrrSkin", OnSkinAttrChanged);
        }

        private void OnSkinAttrChanged()
        {
            ApplySkin(SkinIndex, redraw: true);
        }

        /// <summary>Advance to the next skin (wraps). Server-side; syncs to clients via the
        /// watched attribute. No-op if fewer than 2 skins are defined.</summary>
        public bool CycleSkin()
        {
            if (_skins.Count < 2) return false;
            int next = (SkinIndex + 1) % _skins.Count;
            SkinIndex = next; // setting the watched attr triggers the modified listener -> reskin
            entity.WatchedAttributes.MarkPathDirty("vrrSkin");
            return true;
        }

        /// <summary>Go to the PREVIOUS skin (wraps). Server-side; syncs to clients. No-op if
        /// fewer than 2 skins are defined.</summary>
        public bool CyclePrev()
        {
            if (_skins.Count < 2) return false;
            int prev = (SkinIndex - 1 + _skins.Count) % _skins.Count;
            SkinIndex = prev;
            entity.WatchedAttributes.MarkPathDirty("vrrSkin");
            return true;
        }

        /// <summary>Apply skin i's texture overrides, then optionally re-tesselate.
        ///
        /// Two things make this safe:
        ///  1. We give THIS entity its own copy of the client texture dictionary the first
        ///     time we touch it, so reskinning one car never mutates the shared entity-type
        ///     Properties (which would repaint every car of that type).
        ///  2. Each override CompositeTexture is BAKED into the entity atlas before it's
        ///     handed to the renderer. An unbaked texture makes the tesselator's
        ///     TextureSource ctor throw a NullReferenceException (the crash you hit). The
        ///     bake is fully guarded: if the atlas API doesn't resolve, we skip that override
        ///     rather than ever handing the renderer an unbaked texture.
        /// Server side has no atlas/renderer, so it just keeps the persisted index.</summary>
        private void ApplySkin(int i, bool redraw)
        {
            if (_skins.Count == 0) { VrrDebug.Log(entity.World, "Skin: no skins parsed"); return; }
            if (i < 0 || i >= _skins.Count) i = 0;

            var capi = entity.World?.Api as ICoreClientAPI;
            if (capi == null) { VrrDebug.Log(entity.World, "Skin: server side, index={0} only", i); return; }

            var client = entity.Properties?.Client;
            if (client == null) { VrrDebug.Log(entity.World, "Skin: no client props"); return; }

            // (1) Per-entity texture dictionary so we don't repaint the whole entity type.
            if (!_ownTextures)
            {
                var copy = new Dictionary<string, CompositeTexture>();
                if (client.Textures != null)
                    foreach (var kv in client.Textures) copy[kv.Key] = kv.Value;
                client.Textures = copy;
                _ownTextures = true;
            }
            if (client.Textures == null) return;

            int applied = 0;
            foreach (var kv in _skins[i].Textures)
            {
                try
                {
                    var ct = kv.Value.Clone();
                    capi.EntityTextureAtlas.GetOrInsertTexture(ct, out int subId, out _);
                    VrrDebug.Log(entity.World, "Skin bake: key={0} tex={1} subId={2} baked={3}",
                        kv.Key, ct.Base, subId, ct.Baked != null);
                    if (subId < 0) continue;
                    if (ct.Baked == null)
                        ct.Baked = new BakedCompositeTexture { TextureSubId = subId, BakedName = ct.Base };

                    client.Textures[kv.Key] = ct;
                    applied++;
                }
                catch (System.Exception ex)
                {
                    VrrDebug.Log(entity.World, "Skin bake FAILED: {0}", ex.Message);
                }
            }

            VrrDebug.Log(entity.World, "Skin applied: index={0} name={1} keysApplied={2} redraw={3}",
                i, _skins[i].Name, applied, redraw);

            if (redraw)
            {
                entity.MarkShapeModified();

                // MarkShapeModified re-meshes but can leave the renderer's cached TextureSource
                // in place, so the new (correctly-baked) texture never shows. Force the entity
                // shape renderer to fully re-tesselate, which rebuilds the texture source too.
                try
                {
                    if (entity.Properties?.Client?.Renderer is Vintagestory.GameContent.EntityShapeRenderer esr)
                    {
                        // OnEntityLoaded() forces a full re-tesselation (mesh + texture source)
                        // and is the public entry point the engine itself uses; this is what
                        // makes the newly-baked skin texture actually show.
                        esr.OnEntityLoaded();
                        VrrDebug.Log(entity.World, "Skin: forced renderer re-tesselation");
                    }
                    else
                    {
                        VrrDebug.Log(entity.World, "Skin: renderer is {0}, not EntityShapeRenderer",
                            entity.Properties?.Client?.Renderer?.GetType().Name ?? "null");
                    }
                }
                catch (System.Exception ex)
                {
                    VrrDebug.Log(entity.World, "Skin: re-tesselation failed: {0}", ex.Message);
                }
            }
        }

        // ── JSON parsing & texture snapshotting ──────────────────────────────────

        private void ParseSkins(JsonObject attributes)
        {
            _skins.Clear();
            var arr = attributes?["skins"];
            if (arr == null || !arr.Exists) return;

            // Skin JSON format (flat, parseable with the standard JsonObject helpers — no
            // Newtonsoft dependency):
            //   { "name": "rust", "key": "red", "texture": "game:block/metal/plate/rusty" }
            // A skin overrides exactly one texture key ("key") with one asset ("texture").
            // One override per skin is enough for these vehicles (the body/paint key). If a
            // skin needs several overrides later, "extra" can hold additional key/texture
            // pairs.
            var skinArr = arr.AsArray();
            if (skinArr == null) return;
            foreach (var sk in skinArr)
            {
                var def = new SkinDef { Name = sk["name"].AsString("skin") };
                string? key = sk["key"].AsString(null);
                string? tex = sk["texture"].AsString(null);
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(tex))
                    def.Textures[key] = new CompositeTexture(new AssetLocation(tex));

                // Optional additional overrides: parallel arrays "extraKeys"/"extraTextures".
                var ek = sk["extraKeys"].AsArray<string>(null);
                var et = sk["extraTextures"].AsArray<string>(null);
                if (ek != null && et != null)
                {
                    int n = System.Math.Min(ek.Length, et.Length);
                    for (int i = 0; i < n; i++)
                    {
                        string? eki = ek[i];
                        string? eti = et[i];
                        if (!string.IsNullOrEmpty(eki) && !string.IsNullOrEmpty(eti))
                            def.Textures[eki] = new CompositeTexture(new AssetLocation(eti));
                    }
                }
                _skins.Add(def);
            }
        }

        private class SkinDef
        {
            public string Name = "skin";
            public Dictionary<string, CompositeTexture> Textures = new Dictionary<string, CompositeTexture>();
        }
    }
}
