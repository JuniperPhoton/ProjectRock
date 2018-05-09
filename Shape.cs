public class Shape
{
    public static Shape CreateFromLine(string line)
    {
        var split = line.Split(',');
        if (split.Length != 2)
        {
            return null;
        }

        var shape = new Shape();

        shape.Id = split[0].Replace("\"", "");
        shape.Url = split[1].Replace("\"", "");

        if (shape.Url.Contains(".svg"))
        {
            shape.Extension = "svg";
        }
        else
        {
            shape.Extension = "png";
        }

        return shape;
    }

    public string Id { get; set; }
    public string Url { get; set; }
    public string OutputPath { get; set; }
    public string Extension { get; set; }

    public bool IsRaster
    {
        get
        {
            return Extension == "png";
        }
    }

    private Shape()
    {
        // private constructor
    }

    public string CreateUpdateStatement()
    {
        return $"update `butter_icon` SET `thumbtail` = https://media-shape.bybutter.com/{Id}.{Extension} WHERE `icon_id` = {Id}";
    }

    public override string ToString()
    {
        return $"\"{Id}\",\"{Url}\"";
    }
}