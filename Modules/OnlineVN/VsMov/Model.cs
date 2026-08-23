using System.Collections.Generic;

namespace VsMov;

public sealed class VsSearchResponse
{
    public bool status { get; set; }
    public List<VsMovie> items { get; set; }
}

public sealed class VsDetailResponse
{
    public bool status { get; set; }
    public string msg { get; set; }
    public VsMovie movie { get; set; }
    public List<VsEpisodeServer> episodes { get; set; }
}

public sealed class VsMovie
{
    public VsExternalId tmdb { get; set; }
    public VsExternalId imdb { get; set; }
    public string _id { get; set; }
    public string name { get; set; }
    public string slug { get; set; }
    public string origin_name { get; set; }
    public string content { get; set; }
    public string type { get; set; }
    public string status { get; set; }
    public string thumb_url { get; set; }
    public string poster_url { get; set; }
    public string time { get; set; }
    public string episode_current { get; set; }
    public string episode_total { get; set; }
    public string quality { get; set; }
    public string lang { get; set; }
    public int year { get; set; }
}

public sealed class VsExternalId
{
    public string id { get; set; }
    public string type { get; set; }
    public int? season { get; set; }
    public double vote_average { get; set; }
    public int vote_count { get; set; }
}

public sealed class VsEpisodeServer
{
    public string server_name { get; set; }
    public List<VsEpisode> server_data { get; set; }
}

public sealed class VsEpisode
{
    public string name { get; set; }
    public string slug { get; set; }
    public string filename { get; set; }
    public string link_embed { get; set; }
    public string link_m3u8 { get; set; }
    public int season_number { get; set; }
    public int episode_number { get; set; }
}
