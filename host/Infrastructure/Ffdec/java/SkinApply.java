import com.jpexs.decompiler.flash.SWF;
import com.jpexs.decompiler.flash.abc.ABC;
import com.jpexs.decompiler.flash.abc.ScriptPack;
import com.jpexs.decompiler.flash.abc.avm2.instructions.AVM2Instruction;
import com.jpexs.decompiler.flash.abc.avm2.instructions.AVM2Instructions;
import com.jpexs.decompiler.flash.abc.types.MethodBody;
import com.jpexs.decompiler.flash.abc.types.traits.Trait;
import com.jpexs.decompiler.flash.abc.types.traits.TraitClass;
import com.jpexs.decompiler.flash.abc.types.traits.TraitMethodGetterSetter;
import com.jpexs.decompiler.flash.tags.DefineSpriteTag;
import com.jpexs.decompiler.flash.tags.Tag;
import com.jpexs.decompiler.flash.tags.base.CharacterIdTag;
import com.jpexs.decompiler.flash.tags.base.CharacterTag;
import com.jpexs.decompiler.flash.tags.base.PlaceObjectTypeTag;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Copies sprites from .bmod packs into a game SWF, matching them by SymbolClass name
 * or export name. Each replaced sprite keeps the game's character id so the
 * PlaceObject tags already in the game SWF still point at it. With --colors it also
 * rewrites the recolour palette each patched body part is tinted from.
 *
 * Usage: SkinApply [--colors file] gameSwf outSwf pack.bmod... -- spriteName...
 */
public class SkinApply {
    private static final String USAGE =
            "Usage: SkinApply [--colors <file>] <gameSwf> <outSwf> <bmod>... -- <spriteName>...";

    /** Instruction slot where a frame1 palette starts: getlocal0, pushscope, getlocal0. */
    private static final int FIRST_PUSH = 3;

    /** SWF stores character ids in 16 bits, so the scratch range starts at the top. */
    private static final int FIRST_SCRATCH_ID = 65535;

    /** Real ids stay below this, which leaves the rest of the range for scratch ids. */
    private static final int LAST_REAL_ID = 60000;

    private final SWF game;
    private final List<SWF> packs;

    /**
     * Bones and SFX pack each clip's parts in consecutive ids just before the sprite.
     * Native hover/sig code reads those ids, so we overwrite them in place. Gfx atlases
     * share ids across many skins — overwriting them there swapped faces and bodies.
     */
    private final boolean keepDestChildIds;

    /** Free id for art the game does not have yet. Always above every id in use. */
    private int nextFreeId;

    /**
     * Tags this run put into the game SWF, by destination id. FFDec caches the
     * character list, and we only refresh that cache once at the end, so this is
     * where we look up a tag we wrote a moment ago.
     */
    private final Map<Integer, Tag> written = new LinkedHashMap<>();

    private SkinApply(SWF game, List<SWF> packs, boolean keepDestChildIds) {
        this.game = game;
        this.packs = packs;
        this.keepDestChildIds = keepDestChildIds;
        var highest = game.getNextCharacterId();
        for (var pack : packs) {
            highest = Math.max(highest, pack.getNextCharacterId());
        }

        this.nextFreeId = highest;
    }

    public static void main(String[] args) {
        File colorsFile = null;
        if (args.length > 2 && args[0].equals("--colors")) {
            colorsFile = new File(args[1]);
            args = Arrays.copyOfRange(args, 2, args.length);
        }

        var separator = indexOf(args, "--");
        if (args.length < 5 || separator < 3 || separator == args.length - 1) {
            System.err.println(USAGE);
            System.exit(1);
            return;
        }

        try {
            run(args, separator, colorsFile);
        } catch (ApplyError error) {
            System.err.println(error.getMessage());
            System.exit(2);
        } catch (Exception error) {
            System.err.println("Apply failed: " + error);
            System.exit(3);
        }
    }

    private static void run(String[] args, int separator, File colorsFile) throws Exception {
        var outFile = new File(args[1]);
        var names = distinct(args, separator + 1, args.length);

        var packs = new ArrayList<SWF>();
        for (var i = 2; i < separator; i++) {
            packs.add(readSwf(new File(args[i])));
        }

        var gameFile = new File(args[0]);
        var apply = new SkinApply(
                readSwf(gameFile), packs, keepsPackedChildIds(gameFile.getName()));
        var applied = new ArrayList<String>();
        var skipped = 0;
        for (var name : names) {
            if (apply.replaceSprite(name)) {
                applied.add(name);
            } else {
                skipped++;
            }
        }

        apply.refreshLookups();
        apply.checkEveryReferenceResolves(applied);

        var palettes = readPalettes(colorsFile);
        for (var palette : palettes.entrySet()) {
            apply.replacePalette(palette.getKey(), palette.getValue());
        }

        apply.writeTo(outFile);
        System.out.println("Replaced " + (names.size() - skipped) + " sprite(s) and "
                + palettes.size() + " palette(s). Skipped " + skipped + ".");
    }

    /**
     * Replaces one game sprite with the art of the first pack that carries that name.
     * Returns false when the pack's animation timeline does not match the game, so we
     * leave the vanilla sprite rather than write ids the native Bones/SFX code rejects.
     */
    private boolean replaceSprite(String name) throws Exception {
        var target = findNamed(game, name);
        if (target == null) {
            throw new ApplyError("Game SWF has no sprite named " + name);
        }

        for (var pack : packs) {
            var source = findNamed(pack, name);
            if (source != null) {
                if (keepDestChildIds
                        && (!sameKind(source, target) || !sameTimeline(source, target))) {
                    return false;
                }

                copyTree(pack, source, target);
                return true;
            }
        }

        throw new ApplyError("Pack has no sprite named " + name);
    }

    /**
     * Copies a pack sprite into the game SWF. The root keeps the game's character id.
     * Bones/SFX children reuse the packed dest ids; Gfx children get new ids.
     */
    private void copyTree(SWF pack, CharacterTag srcRoot, CharacterTag destRoot)
            throws Exception {
        var idMap = new LinkedHashMap<Integer, Integer>();
        if (keepDestChildIds) {
            pairCharacters(pack, srcRoot, destRoot, idMap);
        } else {
            idMap.put(srcRoot.getCharacterId(), destRoot.getCharacterId());
        }

        var slots = new ArrayList<Slot>();
        for (var sourceId : dependencyOrder(pack, srcRoot)) {
            if (pack.getCharacter(sourceId) == null) {
                continue;
            }

            if (sourceId == srcRoot.getCharacterId()) {
                slots.add(new Slot(sourceId, destRoot.getCharacterId(), true));
                continue;
            }

            var destId = idMap.get(sourceId);
            if (destId != null) {
                slots.add(new Slot(sourceId, destId, true));
                continue;
            }

            if (!keepDestChildIds) {
                var shared = matchInGame(pack, sourceId);
                if (shared != null) {
                    idMap.put(sourceId, shared.getCharacterId());
                    slots.add(new Slot(sourceId, shared.getCharacterId(), false));
                    continue;
                }
            }

            if (nextFreeId >= LAST_REAL_ID) {
                throw new ApplyError("This game SWF has too many characters to patch.");
            }

            destId = nextFreeId;
            nextFreeId++;
            idMap.put(sourceId, destId);
            slots.add(new Slot(sourceId, destId, true));
        }

        var keepId = destRoot.getCharacterId();
        for (var slot : slots) {
            if (!slot.write) {
                continue;
            }

            var clone = pack.getCharacter(slot.sourceId).cloneTag();
            clone.setSwf(game, true);
            if (clone instanceof CharacterIdTag) {
                ((CharacterIdTag) clone).setCharacterId(slot.destId);
            }

            retargetReferences(clone, idMap);
            markModified(clone);
            insert(clone, slot.destId, keepId);
        }
    }

    /**
     * Walks PlaceObject slots in lockstep so pack part i overwrites game part i.
     * Shared pack parts keep the first game id they were paired with.
     */
    private void pairCharacters(
            SWF pack, CharacterTag source, CharacterTag dest, Map<Integer, Integer> idMap) {
        if (source == null || dest == null || !sameKind(source, dest)) {
            return;
        }

        if (idMap.containsKey(source.getCharacterId())) {
            return;
        }

        idMap.put(source.getCharacterId(), dest.getCharacterId());
        if (!(source instanceof DefineSpriteTag) || !(dest instanceof DefineSpriteTag)) {
            return;
        }

        var packPlaced = placedIds((DefineSpriteTag) source);
        var destPlaced = placedIds((DefineSpriteTag) dest);
        if (packPlaced.size() != destPlaced.size()) {
            return;
        }

        for (var i = 0; i < packPlaced.size(); i++) {
            pairCharacters(
                    pack,
                    pack.getCharacter(packPlaced.get(i)),
                    game.getCharacter(destPlaced.get(i)),
                    idMap);
        }
    }

    /** True when both clips have the same Place/Remove/ShowFrame/label sequence. */
    private static boolean sameTimeline(CharacterTag source, CharacterTag dest) {
        if (!(source instanceof DefineSpriteTag) || !(dest instanceof DefineSpriteTag)) {
            return true;
        }

        var packSprite = (DefineSpriteTag) source;
        var destSprite = (DefineSpriteTag) dest;
        if (packSprite.frameCount != destSprite.frameCount) {
            return false;
        }

        var packTags = packSprite.getTags();
        var destTags = destSprite.getTags();
        if (packTags.size() != destTags.size()) {
            return false;
        }

        for (var i = 0; i < packTags.size(); i++) {
            if (!sameTimelineTag(packTags.get(i), destTags.get(i))) {
                return false;
            }
        }

        return true;
    }

    private static boolean sameTimelineTag(Tag packTag, Tag destTag) {
        if (!packTag.getClass().equals(destTag.getClass())) {
            return false;
        }

        if (packTag instanceof PlaceObjectTypeTag && destTag instanceof PlaceObjectTypeTag) {
            return ((PlaceObjectTypeTag) packTag).getDepth()
                    == ((PlaceObjectTypeTag) destTag).getDepth();
        }

        return true;
    }

    private static boolean sameKind(CharacterTag source, CharacterTag dest) {
        return (source instanceof DefineSpriteTag) == (dest instanceof DefineSpriteTag);
    }

    private static List<Integer> placedIds(DefineSpriteTag sprite) {
        var ids = new ArrayList<Integer>();
        for (var tag : sprite.getTags()) {
            if (tag instanceof PlaceObjectTypeTag) {
                var id = ((PlaceObjectTypeTag) tag).getCharacterId();
                if (id >= 0) {
                    ids.add(id);
                }
            }
        }

        return ids;
    }

    private static void markModified(Tag tag) {
        tag.setModified(true);
        if (tag instanceof DefineSpriteTag) {
            for (var inner : ((DefineSpriteTag) tag).getTags()) {
                markModified(inner);
            }
        }
    }

    private void insert(Tag clone, int destId, int keepId) {
        var existing = tagAt(destId);
        if (existing != null) {
            game.replaceTag(existing, clone);
        } else {
            var keep = tagAt(keepId);
            if (keep == null) {
                game.addTag(clone);
            } else {
                SWF.addTagBefore(clone, keep);
            }
        }

        written.put(destId, clone);
    }

    private Tag tagAt(int characterId) {
        var mine = written.get(characterId);
        return mine != null ? mine : game.getCharacter(characterId);
    }

    /**
     * Points a copied tag at its new character ids. It takes two passes through a
     * scratch range because one pass can rewrite the same reference twice: with
     * 5 -> 9 and 9 -> 12 in the same map, the original 5 would end up as 12.
     */
    private static void retargetReferences(Tag tag, Map<Integer, Integer> idMap)
            throws ApplyError {
        var parked = new LinkedHashMap<Integer, Integer>();
        var scratchId = FIRST_SCRATCH_ID;
        for (var entry : idMap.entrySet()) {
            if (entry.getKey().equals(entry.getValue())) {
                continue;
            }

            if (scratchId <= LAST_REAL_ID) {
                throw new ApplyError("This sprite has too many parts to patch.");
            }

            tag.replaceCharacter(entry.getKey(), scratchId);
            parked.put(scratchId, entry.getValue());
            scratchId--;
        }

        for (var entry : parked.entrySet()) {
            tag.replaceCharacter(entry.getKey(), entry.getValue());
        }
    }

    /** Rebuilds the name and character lookups after the tag list changed. */
    private void refreshLookups() {
        game.assignExportNamesToSymbols();
        game.assignClassesToSymbols();
        game.updateCharacters();
    }

    /**
     * Refuses to write a SWF where a patched sprite points at a character that is not
     * there. The game reads that reference blind, so a dangling id is a crash.
     */
    private void checkEveryReferenceResolves(List<String> names) throws ApplyError {
        for (var name : names) {
            var character = findNamed(game, name);
            if (character == null) {
                throw new ApplyError("Sprite " + name + " disappeared while patching.");
            }

            Set<Integer> needed = new LinkedHashSet<>();
            character.getNeededCharactersDeep(needed, new LinkedHashSet<>());
            for (var id : needed) {
                if (game.getCharacter(id) == null) {
                    throw new ApplyError(
                            "Patched sprite " + name + " points at missing art (id " + id + ").");
                }
            }
        }
    }

    /**
     * Rewrites the colour list a body part is recoloured from. The vanilla frame1 method
     * is a fixed shape — push one number per colour, then newarray — so we swap the
     * pushes instead of compiling ActionScript, which would need Adobe's SWC.
     */
    private void replacePalette(String className, List<Integer> colors) throws ApplyError {
        var script = scriptFor(className);
        if (script == null) {
            throw new ApplyError("Game SWF has no colour script named " + className);
        }

        var abc = script.abc;
        var body = frame1Body(abc, script);
        if (body == null) {
            throw new ApplyError("Colour script " + className + " has no frame1 to patch.");
        }

        var code = body.getCode();
        var newArrayAt = indexOfNewArray(code.code);
        if (newArrayAt < FIRST_PUSH || !onlyPushesBefore(code.code, newArrayAt)) {
            throw new ApplyError("Colour script " + className + " is not a plain colour list.");
        }

        // Packs often ship the palette the game already has. Leaving the bytecode alone
        // then is both faster and safer than rewriting it to the same thing.
        if (colors.equals(currentPalette(abc, code.code, newArrayAt))) {
            return;
        }

        for (var i = newArrayAt - 1; i >= FIRST_PUSH; i--) {
            code.removeInstruction(i, body);
        }

        for (var i = 0; i < colors.size(); i++) {
            var push = new AVM2Instruction(
                    0,
                    AVM2Instructions.PushInt,
                    new int[] {abc.constants.getIntId(colors.get(i), true)});
            code.insertInstruction(FIRST_PUSH + i, push, body);
        }

        code.code.get(FIRST_PUSH + colors.size()).operands[0] = colors.size();

        // One slot per colour plus the scope object the pushes sit on top of.
        body.max_stack = Math.max(body.max_stack, colors.size() + 2);
        body.setModified();
        ((Tag) abc.parentTag).setModified(true);
    }

    private ScriptPack scriptFor(String className) {
        for (var script : game.getAS3Packs()) {
            if (script.getClassPath().toString().equals(className)) {
                return script;
            }
        }

        return null;
    }

    private static MethodBody frame1Body(ABC abc, ScriptPack script) {
        for (var traitIndex : script.traitIndices) {
            Trait trait = abc.script_info.get(script.scriptIndex).traits.traits.get(traitIndex);
            if (!(trait instanceof TraitClass)) {
                continue;
            }

            var classIndex = ((TraitClass) trait).class_info;
            for (var member : abc.instance_info.get(classIndex).instance_traits.traits) {
                var name = abc.constants.getString(
                        abc.constants.getMultiname(member.name_index).name_index);
                if (member instanceof TraitMethodGetterSetter && "frame1".equals(name)) {
                    return abc.findBody(((TraitMethodGetterSetter) member).method_info);
                }
            }
        }

        return null;
    }

    private static int indexOfNewArray(List<AVM2Instruction> code) {
        for (var i = 0; i < code.size(); i++) {
            if ("newarray".equals(code.get(i).definition.instructionName)) {
                return i;
            }
        }

        return -1;
    }

    /** The colours already in the method, or null when a push is not a pool integer. */
    private static List<Integer> currentPalette(
            ABC abc, List<AVM2Instruction> code, int newArrayAt) {
        var colors = new ArrayList<Integer>();
        for (var i = FIRST_PUSH; i < newArrayAt; i++) {
            var instruction = code.get(i);
            if (!"pushint".equals(instruction.definition.instructionName)) {
                return null;
            }

            colors.add(abc.constants.getInt(instruction.operands[0]));
        }

        return colors;
    }

    private static boolean onlyPushesBefore(List<AVM2Instruction> code, int newArrayAt) {
        for (var i = FIRST_PUSH; i < newArrayAt; i++) {
            if (!code.get(i).definition.instructionName.startsWith("push")) {
                return false;
            }
        }

        return true;
    }

    /** Reads the "ClassName=1,2,3" lines the host wrote, in file order. */
    private static Map<String, List<Integer>> readPalettes(File colorsFile) throws Exception {
        Map<String, List<Integer>> palettes = new LinkedHashMap<>();
        if (colorsFile == null) {
            return palettes;
        }

        for (var line : Files.readAllLines(colorsFile.toPath(), StandardCharsets.UTF_8)) {
            var split = line.indexOf('=');
            if (split <= 0) {
                continue;
            }

            var colors = new ArrayList<Integer>();
            var listed = line.substring(split + 1).trim();
            if (!listed.isEmpty()) {
                for (var part : listed.split(",")) {
                    colors.add(Integer.valueOf(part.trim()));
                }
            }

            palettes.put(line.substring(0, split), colors);
        }

        return palettes;
    }

    private void writeTo(File outFile) throws Exception {
        game.setModified(true);
        var parent = outFile.getParentFile();
        if (parent != null) {
            parent.mkdirs();
        }

        try (var output = new FileOutputStream(outFile)) {
            game.saveTo(output);
        }
    }

    /** Brawlhalla names its art through SymbolClass, older packs through ExportAssets. */
    private static CharacterTag findNamed(SWF swf, String name) {
        if (name == null || name.isEmpty()) {
            return null;
        }

        var byClass = swf.getCharacterByClass(name);
        return byClass != null ? byClass : swf.getCharacterByExportName(name);
    }

    static boolean keepsPackedChildIds(String fileName) {
        var name = fileName.toLowerCase();
        return name.startsWith("bones_") || name.startsWith("sfx_");
    }

    /** Finds the game character that carries the same name as a pack character. */
    private CharacterTag matchInGame(SWF pack, int sourceId) {
        var byExportName = findNamed(game, pack.getExportName(sourceId));
        if (byExportName != null) {
            return byExportName;
        }

        var classNames = pack.getCharacter(sourceId).getClassNames();
        if (classNames != null) {
            for (var className : classNames) {
                var match = findNamed(game, className);
                if (match != null) {
                    return match;
                }
            }
        }

        return null;
    }

    /** Children before parents, walking each character once. */
    private static List<Integer> dependencyOrder(SWF pack, CharacterTag root) {
        var order = new ArrayList<Integer>();
        collectDepthFirst(pack, root.getCharacterId(), new LinkedHashSet<>(), order);
        return order;
    }

    private static void collectDepthFirst(
            SWF pack, int id, Set<Integer> seen, List<Integer> order) {
        if (!seen.add(id)) {
            return;
        }

        var character = pack.getCharacter(id);
        if (character != null) {
            Set<Integer> children = new LinkedHashSet<>();
            character.getNeededCharacters(children, new LinkedHashSet<>(), pack);
            for (var child : children) {
                if (child != id) {
                    collectDepthFirst(pack, child, seen, order);
                }
            }
        }

        order.add(id);
    }

    private static SWF readSwf(File file) throws Exception {
        try (var input = new FileInputStream(file)) {
            return new SWF(input, false);
        }
    }

    private static int indexOf(String[] args, String value) {
        for (var i = 0; i < args.length; i++) {
            if (args[i].equals(value)) {
                return i;
            }
        }

        return -1;
    }

    private static List<String> distinct(String[] args, int from, int to) {
        var names = new ArrayList<String>();
        for (var i = from; i < to; i++) {
            if (!names.contains(args[i])) {
                names.add(args[i]);
            }
        }

        return names;
    }

    /** Where one pack character lands in the game SWF. */
    private static final class Slot {
        final int sourceId;
        final int destId;

        /** False when the game already has this art and we only reuse its id. */
        final boolean write;

        Slot(int sourceId, int destId, boolean write) {
            this.sourceId = sourceId;
            this.destId = destId;
            this.write = write;
        }
    }

    /** A message the user should read; the host restores the snapshot after it. */
    private static final class ApplyError extends Exception {
        ApplyError(String message) {
            super(message);
        }
    }
}
