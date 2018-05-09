public class Shape
{
    public string Id { get; set; }
    public string Url { get; set; }
    public string OutputPath { get; set; }

    public string CreateLog()
    {
        return $"update `butter_icon` SET `thumbtail` = https://media-shape.bybutter.com/{Id}.png WHERE `icon_id` = {Id}";
    }

    public override string ToString()
    {
        return $"\"{Id}\",\"{Url}\"";
    }
}