// Ghidra headless post-script for BO2 script/notify reverse engineering.
// Usage:
//   analyzeHeadless <projectDir> BO2Recon -import <t6zm.exe> -postScript BO2ScriptRecon.java <outputPath>

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.util.task.TaskMonitor;

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class BO2ScriptRecon extends GhidraScript {
    private PrintWriter out;

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        File outputFile = args.length > 0
            ? new File(args[0])
            : new File("bo2-ghidra-recon.txt");
        File parent = outputFile.getParentFile();
        if (parent != null) {
            parent.mkdirs();
        }

        out = new PrintWriter(outputFile, StandardCharsets.UTF_8.name());
        try {
            out.printf("program=%s%n", currentProgram.getExecutablePath());
            out.printf("imageBase=%s%n", currentProgram.getImageBase());
            out.printf("language=%s%n", currentProgram.getLanguageID());
            out.printf("compiler=%s%n%n", currentProgram.getCompilerSpec().getCompilerSpecID());

            Map<String, Long> targets = new LinkedHashMap<>();
            targets.put("local_vm_notify_entry", 0x008F31D0L);
            targets.put("public_vm_notify_candidate", 0x008F3620L);
            targets.put("sl_get_string_of_size_candidate", 0x00418B40L);
            targets.put("scr_find_variable_candidate", 0x006BFB30L);
            targets.put("scr_get_variable_value_candidate", 0x00485950L);
            targets.put("scr_get_variable_value_address_candidate", 0x0067C1B0L);
            targets.put("scr_set_variable_field_candidate", 0x0058F9E0L);
            targets.put("scr_find_object_candidate", 0x00474EA0L);
            targets.put("script_string_data_pointer", 0x02BF83A4L);
            targets.put("scr_var_glob_candidate", 0x02DEA400L);

            for (Map.Entry<String, Long> target : targets.entrySet()) {
                dumpAddress(target.getKey(), target.getValue());
            }

            dumpCallersTo(addr(0x008F31D0L), "callers_to_vm_notify");

            String[] needles = new String[] {
                "randomization_done",
                "user_grabbed_weapon",
                "weapon_string",
                "grab_weapon_name",
                "zbarrier",
                "giveweapon",
                "takeweapon",
                "chest_accessed"
            };

            for (String needle : needles) {
                searchAscii(needle);
            }
        }
        finally {
            out.close();
        }
    }

    private Address addr(long value) {
        return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(value);
    }

    private void dumpAddress(String label, long value) throws Exception {
        Address address = addr(value);
        out.printf("== %s %s ==%n", label, address);
        Memory memory = currentProgram.getMemory();
        MemoryBlock block = memory.getBlock(address);
        out.printf("block=%s readable=%s executable=%s%n",
            block == null ? "<none>" : block.getName(),
            block != null && block.isRead(),
            block != null && block.isExecute());

        Function function = findContainingFunction(address);
        if (function != null) {
            out.printf("function=%s entry=%s body=%s%n",
                function.getName(),
                function.getEntryPoint(),
                function.getBody());
            dumpReferencesTo(function.getEntryPoint(), "refs_to_function_entry");
            dumpFunctionInstructions(function, 80);
            dumpDecompile(function, 180);
        }
        else {
            out.println("function=<none>");
            dumpInstructionsAt(address, 40);
            dumpReferencesTo(address, "refs_to_address");
        }

        out.println();
    }

    private Function findContainingFunction(Address address) {
        Function function = currentProgram.getFunctionManager().getFunctionContaining(address);
        if (function != null) {
            return function;
        }

        return currentProgram.getFunctionManager().getFunctionAt(address);
    }

    private void dumpFunctionInstructions(Function function, int maxInstructions) {
        out.println("-- instructions --");
        int count = 0;
        AddressSetView body = function.getBody();
        for (Instruction instruction : currentProgram.getListing().getInstructions(body, true)) {
            out.printf("%s  %s%n", instruction.getAddress(), instruction);
            count++;
            if (count >= maxInstructions) {
                out.printf("... truncated after %d instructions%n", maxInstructions);
                break;
            }
        }
    }

    private void dumpInstructionsAt(Address address, int maxInstructions) {
        out.println("-- instructions_at --");
        Instruction instruction = currentProgram.getListing().getInstructionAt(address);
        if (instruction == null) {
            instruction = currentProgram.getListing().getInstructionContaining(address);
        }

        int count = 0;
        while (instruction != null && count < maxInstructions) {
            out.printf("%s  %s%n", instruction.getAddress(), instruction);
            instruction = instruction.getNext();
            count++;
        }
    }

    private void dumpReferencesTo(Address address, String title) {
        out.printf("-- %s %s --%n", title, address);
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
        int count = 0;
        for (Reference reference : references) {
            out.printf("%s from=%s type=%s operand=%d%n",
                reference.getToAddress(),
                reference.getFromAddress(),
                reference.getReferenceType(),
                reference.getOperandIndex());
            count++;
            if (count >= 50) {
                out.println("... truncated refs");
                break;
            }
        }
        if (count == 0) {
            out.println("<none>");
        }
    }

    private void dumpCallersTo(Address address, String title) {
        out.printf("== %s %s ==%n", title, address);
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
        int count = 0;
        for (Reference reference : references) {
            Address from = reference.getFromAddress();
            Function caller = findContainingFunction(from);
            out.printf("-- caller from=%s type=%s function=%s entry=%s --%n",
                from,
                reference.getReferenceType(),
                caller == null ? "<none>" : caller.getName(),
                caller == null ? "<none>" : caller.getEntryPoint().toString());
            dumpInstructionWindow(from, 12, 24);
            if (caller != null) {
                dumpDecompile(caller, 90);
            }

            count++;
            if (count >= 20) {
                out.println("... truncated callers");
                break;
            }
        }
        if (count == 0) {
            out.println("<none>");
        }
        out.println();
    }

    private void dumpInstructionWindow(Address address, int before, int after) {
        out.println("-- instruction_window --");
        Instruction instruction = currentProgram.getListing().getInstructionContaining(address);
        if (instruction == null) {
            instruction = currentProgram.getListing().getInstructionAt(address);
        }
        if (instruction == null) {
            out.println("<none>");
            return;
        }

        List<Instruction> previous = new ArrayList<>();
        Instruction cursor = instruction;
        for (int i = 0; i < before; i++) {
            cursor = cursor.getPrevious();
            if (cursor == null) {
                break;
            }
            previous.add(cursor);
        }

        Collections.reverse(previous);
        for (Instruction item : previous) {
            out.printf("%s  %s%n", item.getAddress(), item);
        }
        out.printf("%s  %s    ; <-- reference%n", instruction.getAddress(), instruction);

        cursor = instruction;
        for (int i = 0; i < after; i++) {
            cursor = cursor.getNext();
            if (cursor == null) {
                break;
            }
            out.printf("%s  %s%n", cursor.getAddress(), cursor);
        }
    }

    private void dumpDecompile(Function function, int maxLines) {
        out.println("-- decompile --");
        DecompInterface decompiler = new DecompInterface();
        try {
            decompiler.openProgram(currentProgram);
            DecompileResults results = decompiler.decompileFunction(function, 30, TaskMonitor.DUMMY);
            if (!results.decompileCompleted()) {
                out.printf("decompile_failed=%s%n", results.getErrorMessage());
                return;
            }

            String c = results.getDecompiledFunction().getC();
            String[] lines = c.split("\\R");
            for (int i = 0; i < lines.length && i < maxLines; i++) {
                out.println(lines[i]);
            }
            if (lines.length > maxLines) {
                out.println("... truncated decompile");
            }
        }
        catch (Exception ex) {
            out.printf("decompile_exception=%s%n", ex.getMessage());
        }
        finally {
            decompiler.dispose();
        }
    }

    private void searchAscii(String needle) throws Exception {
        out.printf("== ascii_search %s ==%n", needle);
        byte[] bytes = (needle + "\0").getBytes(StandardCharsets.US_ASCII);
        Memory memory = currentProgram.getMemory();
        int count = 0;

        for (MemoryBlock block : memory.getBlocks()) {
            if (!block.isRead() || block.getSize() <= 0) {
                continue;
            }

            Address cursor = block.getStart();
            Address end = block.getEnd();
            while (cursor != null && cursor.compareTo(end) <= 0) {
                Address hit = memory.findBytes(cursor, end, bytes, null, true, monitor);
                if (hit == null) {
                    break;
                }

                out.printf("hit=%s block=%s%n", hit, block.getName());
                dumpReferencesTo(hit, "refs_to_string");
                count++;
                if (count >= 20) {
                    out.println("... truncated string hits");
                    break;
                }

                cursor = hit.add(1);
            }

            if (count >= 20) {
                break;
            }
        }

        if (count == 0) {
            out.println("<none>");
        }
        out.println();
    }
}
