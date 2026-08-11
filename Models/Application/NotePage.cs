using Dujahit.ViewModels;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Dujahit.Models.Application
{
    public static class NotePageScope
    {
        public const string Private = "private";
        public const string Shared = "shared";
        public const string CampaignStory = "campaign_story";
    }

    public static class NotePagePermission
    {
        public const string Read = "read";
        public const string Edit = "edit";
    }

    public class NotePage
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? OwnerUserId { get; set; }
        public string? ParentPageId { get; set; }
        public string Scope { get; set; } = NotePageScope.Private;
        public string Title { get; set; } = "Untitled";
        public string? Icon { get; set; }
        public string ContentMarkdown { get; set; } = "";
        public int SortOrder { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Slug { get; set; }
    }

    public class NotePageShare
    {
        public string PageId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Permission { get; set; } = NotePagePermission.Edit;
        public DateTime SharedAt { get; set; }
    }

    public class NotePageNode
    {
        public NotePage Page { get; set; } = new();
        public List<NotePageNode> Children { get; } = new();
    }
}