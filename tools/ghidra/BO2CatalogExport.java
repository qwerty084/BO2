// Ghidra headless post-script for BO2 reverse-engineering catalogs.
// Usage:
//   analyzeHeadless <projectDir> BO2Recon -process t6zm.exe -postScript BO2CatalogExport.java <outputDir>

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class BO2CatalogExport extends GhidraScript {
    private static class FunctionTarget {
        final String label;
        final long address;
        final String purpose;
        final String parameters;
        final String returnValue;
        final String evidenceLevel;
        final String notes;

        FunctionTarget(
            String label,
            long address,
            String purpose,
            String parameters,
            String returnValue,
            String evidenceLevel,
            String notes) {
            this.label = label;
            this.address = address;
            this.purpose = purpose;
            this.parameters = parameters;
            this.returnValue = returnValue;
            this.evidenceLevel = evidenceLevel;
            this.notes = notes;
        }
    }

    private static class GlobalTarget {
        final String label;
        final long address;
        final String purpose;
        final String shape;
        final String evidenceLevel;
        final String notes;

        GlobalTarget(String label, long address, String purpose, String shape, String evidenceLevel, String notes) {
            this.label = label;
            this.address = address;
            this.purpose = purpose;
            this.shape = shape;
            this.evidenceLevel = evidenceLevel;
            this.notes = notes;
        }
    }

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        File outputDir = args.length > 0 ? new File(args[0]) : new File("artifacts/reverse");
        outputDir.mkdirs();

        List<FunctionTarget> functions = new ArrayList<>();
        functions.add(new FunctionTarget(
            "local_vm_notify_entry",
            0x008f31d0L,
            "Local Steam Zombies vm_notify implementation used as the production notify hook target.",
            "__cdecl int inst, uint ownerId, uint stringValue, void* top",
            "void",
            "strong",
            "Function starts at expected prologue and owns the public candidate address as an interior instruction."));
        functions.add(new FunctionTarget(
            "public_vm_notify_candidate_rejected",
            0x008f3620L,
            "Public T6 vm_notify address candidate rejected for this Steam build.",
            "n/a",
            "n/a",
            "strong",
            "Address lies inside local_vm_notify_entry rather than at a function entry."));
        functions.add(new FunctionTarget(
            "sl_get_string_of_size_candidate",
            0x00418b40L,
            "Script-string interning/resolution helper used to resolve notify names at runtime.",
            "__cdecl const char* name, int user, uint len, int type",
            "uint script string id",
            "strong",
            "Runtime code validates prologue before calling."));
        functions.add(new FunctionTarget(
            "scr_find_variable_candidate",
            0x006bfb30L,
            "Find child variable by script instance, parent id, and name id.",
            "int inst, uint parentId, uint nameId",
            "uint child id / 0 when absent",
            "strong",
            "Called by vm_notify with owner and notify string ids."));
        functions.add(new FunctionTarget(
            "scr_get_variable_value_candidate",
            0x00485950L,
            "Read script variable value for a child/object id.",
            "int inst, uint variableId",
            "uint value/object id",
            "strong",
            "Called immediately after successful scr_find_variable_candidate in vm_notify."));
        functions.add(new FunctionTarget(
            "scr_get_variable_value_address_candidate",
            0x0067c1b0L,
            "Return pointer to a script variable value slot.",
            "int inst, uint variableId",
            "pointer to value storage",
            "medium",
            "Used in vm_notify complex object traversal path."));
        functions.add(new FunctionTarget(
            "scr_set_variable_field_candidate",
            0x0058f9e0L,
            "Assign or link a variable field in the script VM.",
            "int inst, uint parent/object, uint value/object",
            "unknown",
            "medium",
            "Observed in vm_notify branch that creates or updates script object fields."));
        functions.add(new FunctionTarget(
            "scr_find_object_candidate",
            0x00474ea0L,
            "Resolve/find script object metadata by id.",
            "int inst, uint objectId",
            "int/object metadata id",
            "medium",
            "Observed around vm_notify object traversal."));

        List<GlobalTarget> globals = new ArrayList<>();
        globals.add(new GlobalTarget(
            "script_string_data_pointer",
            0x02bf83a4L,
            "Pointer to script string table base.",
            "pointer; entries are 0x18 bytes, text at +0x04",
            "strong",
            "vm_notify decompile references DAT_02bf83a4 for script-string values."));
        globals.add(new GlobalTarget(
            "scr_var_glob_candidate",
            0x02dea400L,
            "Start of script variable global region.",
            "global data region",
            "medium",
            "Nearby per-instance pointer slots and VM globals live in this data range."));
        globals.add(new GlobalTarget(
            "child_bucket_pointer_slot_base",
            0x02defb00L,
            "Per-instance child-variable hash bucket table pointer slots.",
            "pointer slot base + inst*0x200",
            "strong",
            "vm_notify decompile references DAT_02defa00 and companion tooling resolves bucket slots."));
        globals.add(new GlobalTarget(
            "child_variables_pointer_slot_base",
            0x02defb80L,
            "Per-instance child-variable table pointer slots.",
            "pointer slot base + inst*0x200; child entry size 0x1c",
            "strong",
            "vm_notify decompile uses DAT_02defb80 with child id * 0x1c."));
        globals.add(new GlobalTarget(
            "vm_notify_callback_table_base",
            0x02df4170L,
            "Per-instance optional notify callback pointer checked by vm_notify.",
            "code pointer at base + inst*0x42a8",
            "medium",
            "vm_notify calls callback when non-null before variable lookup."));
        globals.add(new GlobalTarget(
            "vm_notify_remap_a",
            0x024bb4ccL,
            "Script string id remapped by vm_notify when inst is 0.",
            "uint16 script string id",
            "medium",
            "vm_notify compares notify stringValue to this global."));
        globals.add(new GlobalTarget(
            "vm_notify_remap_b",
            0x024bb4ceL,
            "Second script string id remapped by vm_notify when inst is 0.",
            "uint16 script string id",
            "medium",
            "vm_notify compares notify stringValue to this global."));
        globals.add(new GlobalTarget(
            "vm_notify_remap_target",
            0x024bb4d0L,
            "Replacement script string id used by vm_notify remap.",
            "uint16 script string id",
            "medium",
            "vm_notify recursively calls itself with this value."));

        writeFunctionCatalog(new File(outputDir, "function-catalog.csv"), functions);
        writeGlobalsCatalog(new File(outputDir, "globals-catalog.csv"), globals);
        writeCallgraphNotes(new File(outputDir, "callgraph-notes.md"), functions);
    }

    private Address addr(long value) {
        return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(value);
    }

    private Function findContainingFunction(Address address) {
        Function function = currentProgram.getFunctionManager().getFunctionContaining(address);
        return function != null ? function : currentProgram.getFunctionManager().getFunctionAt(address);
    }

    private String functionName(Function function) {
        return function == null ? "" : function.getName() + "@" + function.getEntryPoint();
    }

    private String blockInfo(Address address) {
        MemoryBlock block = currentProgram.getMemory().getBlock(address);
        if (block == null) {
            return "";
        }

        return block.getName()
            + " read=" + block.isRead()
            + " write=" + block.isWrite()
            + " execute=" + block.isExecute();
    }

    private String instructionInfo(Address address) {
        Instruction instruction = currentProgram.getListing().getInstructionAt(address);
        if (instruction == null) {
            instruction = currentProgram.getListing().getInstructionContaining(address);
        }

        if (instruction == null) {
            return "";
        }

        return instruction.getAddress() + " " + instruction.toString();
    }

    private String bytesAt(Address address, int length) {
        try {
            byte[] bytes = new byte[length];
            currentProgram.getMemory().getBytes(address, bytes);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.length; i++) {
                if (i > 0) {
                    builder.append(' ');
                }
                builder.append(String.format("%02X", bytes[i] & 0xff));
            }
            return builder.toString();
        }
        catch (Exception ex) {
            return "";
        }
    }

    private String callersTo(Function function, int limit) {
        if (function == null) {
            return "";
        }

        Set<String> callers = new LinkedHashSet<>();
        ReferenceIterator refs = currentProgram.getReferenceManager().getReferencesTo(function.getEntryPoint());
        for (Reference ref : refs) {
            if (!ref.getReferenceType().isCall()) {
                continue;
            }

            Function caller = findContainingFunction(ref.getFromAddress());
            callers.add(functionName(caller) + " from " + ref.getFromAddress());
            if (callers.size() >= limit) {
                break;
            }
        }

        return String.join("; ", callers);
    }

    private String calleesFrom(Function function, int limit) {
        if (function == null) {
            return "";
        }

        Set<String> callees = new LinkedHashSet<>();
        for (Instruction instruction : currentProgram.getListing().getInstructions(function.getBody(), true)) {
            Reference[] refs = instruction.getReferencesFrom();
            for (Reference ref : refs) {
                if (!ref.getReferenceType().isCall()) {
                    continue;
                }

                Function callee = findContainingFunction(ref.getToAddress());
                callees.add(functionName(callee) + " via " + instruction.getAddress());
                if (callees.size() >= limit) {
                    return String.join("; ", callees);
                }
            }
        }

        return String.join("; ", callees);
    }

    private String refsTo(Address address, int limit) {
        List<String> refs = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager().getReferencesTo(address);
        for (Reference ref : iterator) {
            refs.add(ref.getFromAddress() + " " + ref.getReferenceType());
            if (refs.size() >= limit) {
                break;
            }
        }

        return String.join("; ", refs);
    }

    private void writeFunctionCatalog(File output, List<FunctionTarget> targets) throws Exception {
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8.name())) {
            writer.println("label,address,kind,ghidra_function,containing_function,block,instruction_at_address,first_16_bytes,inferred_purpose,callers,callees,key_xrefs,inferred_parameters,inferred_return,evidence_level,build_specific,notes");
            for (FunctionTarget target : targets) {
                Address address = addr(target.address);
                Function function = currentProgram.getFunctionManager().getFunctionAt(address);
                Function containing = findContainingFunction(address);
                writer.println(csv(
                    target.label,
                    hex(target.address),
                    target.label.contains("rejected") ? "rejected_code_candidate" : "code",
                    functionName(function),
                    functionName(containing),
                    blockInfo(address),
                    instructionInfo(address),
                    bytesAt(address, 16),
                    target.purpose,
                    callersTo(function != null ? function : containing, 12),
                    calleesFrom(containing, 20),
                    refsTo(function != null ? function.getEntryPoint() : address, 20),
                    target.parameters,
                    target.returnValue,
                    target.evidenceLevel,
                    "current Steam build 65428 / t6zm.exe MD5 68C62BE753DE8ADF2C2C7B28DB769B99",
                    target.notes));
            }
        }
    }

    private void writeGlobalsCatalog(File output, List<GlobalTarget> targets) throws Exception {
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8.name())) {
            writer.println("label,address,kind,block,first_16_bytes,inferred_purpose,shape,key_xrefs,evidence_level,build_specific,notes");
            for (GlobalTarget target : targets) {
                Address address = addr(target.address);
                writer.println(csv(
                    target.label,
                    hex(target.address),
                    "data",
                    blockInfo(address),
                    bytesAt(address, 16),
                    target.purpose,
                    target.shape,
                    refsTo(address, 30),
                    target.evidenceLevel,
                    "current Steam build 65428 / t6zm.exe MD5 68C62BE753DE8ADF2C2C7B28DB769B99",
                    target.notes));
            }
        }
    }

    private void writeCallgraphNotes(File output, List<FunctionTarget> targets) throws Exception {
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8.name())) {
            writer.println("# t6zm.exe Callgraph Notes");
            writer.println();
            writer.println("- Program: " + currentProgram.getExecutablePath());
            writer.println("- Image base: " + currentProgram.getImageBase());
            writer.println("- Language: " + currentProgram.getLanguageID());
            writer.println("- Compiler spec: " + currentProgram.getCompilerSpec().getCompilerSpecID());
            writer.println("- Scope: static Ghidra 12.0.4 headless output for BO2 Event Monitor targets.");
            writer.println();
            for (FunctionTarget target : targets) {
                Address address = addr(target.address);
                Function containing = findContainingFunction(address);
                writer.println("## " + target.label + " (" + hex(target.address) + ")");
                writer.println();
                writer.println("- Containing function: " + functionName(containing));
                writer.println("- Instruction at address: `" + instructionInfo(address) + "`");
                writer.println("- Purpose: " + target.purpose);
                writer.println("- Callers: " + emptyAsNone(callersTo(containing, 12)));
                writer.println("- Callees: " + emptyAsNone(calleesFrom(containing, 20)));
                writer.println("- Xrefs: " + emptyAsNone(refsTo(containing != null ? containing.getEntryPoint() : address, 20)));
                writer.println();
            }
        }
    }

    private String emptyAsNone(String value) {
        return value == null || value.isEmpty() ? "<none>" : value;
    }

    private String hex(long value) {
        return String.format("0x%08X", value);
    }

    private String csv(String... values) {
        List<String> escaped = new ArrayList<>();
        for (String value : values) {
            escaped.add(csvEscape(value == null ? "" : value));
        }

        return String.join(",", escaped);
    }

    private String csvEscape(String value) {
        String escaped = value.replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }
}
