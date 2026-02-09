using System.Text;

namespace AnimeFolderOrganizer.Services;

public static class ApiPrompt
{
    public static string BuildUserPrompt(string folderInput)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert system for analyzing anime folder names.");
        sb.AppendLine("Your task is to extract metadata from the provided folder names and return the result in strict JSON format.");
        sb.AppendLine();
        sb.AppendLine("Input Format:");
        sb.AppendLine("[Index] \"Folder Name\"");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("1. Identify the anime title in Japanese (TitleJP), Traditional Chinese (TitleTW), Simplified Chinese (TitleCN), and English (TitleEN).");
        sb.AppendLine("2. Extract the Year (e.g., 2023).");
        sb.AppendLine("3. Identify the Type (TV, OVA, Movie, Special). Default to 'TV' if unsure.");
        sb.AppendLine("4. Assign a Confidence score (0.0 to 1.0).");
        sb.AppendLine("5. Ignore technical tags in brackets (resolution, codec, source, episode ranges). Use the clean title only.");
        sb.AppendLine("6. If a field is unknown, return an empty string instead of guessing.");
        sb.AppendLine("7. Return ONLY a JSON object with a property 'items' containing the list of results.");
        sb.AppendLine("8. Do NOT include markdown formatting (```json) in the output if possible, just raw JSON.");
        sb.AppendLine();
        sb.AppendLine("JSON Schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"items\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"index\": 0,");
        sb.AppendLine("      \"id\": \"unique_id_or_hash\",");
        sb.AppendLine("      \"titleJP\": \"Japanese Title\",");
        sb.AppendLine("      \"titleCN\": \"Simplified Chinese Title\",");
        sb.AppendLine("      \"titleTW\": \"Traditional Chinese Title\",");
        sb.AppendLine("      \"titleEN\": \"English Title\",");
        sb.AppendLine("      \"type\": \"TV\",");
        sb.AppendLine("      \"year\": 2023,");
        sb.AppendLine("      \"confidence\": 0.95");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Folder List to Analyze:");
        sb.AppendLine(folderInput);

        return sb.ToString();
    }
}
