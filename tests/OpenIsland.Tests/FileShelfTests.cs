using System;
using System.IO;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class FileShelfTests : IDisposable
{
    public FileShelfTests()
    {
        FileShelf.Clear();
    }

    public void Dispose()
    {
        FileShelf.Clear();
    }

    [Fact]
    public void FormatSize_FormatsCorrectly()
    {
        Assert.Equal("0 Б", FileShelf.FormatSize(0));
        Assert.Equal("500 Б", FileShelf.FormatSize(500));
        Assert.Equal("1.5 КБ", FileShelf.FormatSize(1536));
        Assert.Equal("2.0 МБ", FileShelf.FormatSize(2 * 1024 * 1024));
        Assert.Equal("1.50 ГБ", FileShelf.FormatSize((long)(1.5 * 1024 * 1024 * 1024)));
    }

    [Fact]
    public void AddFiles_AddsExistingFilesAndIgnoresMissing()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Hello FileShelf");
            int added = FileShelf.AddFiles([tempFile, @"C:\non_existent_folder_xyz\file.txt", ""]);

            Assert.Equal(1, added);
            Assert.Equal(1, FileShelf.Count);
            Assert.Equal(tempFile, FileShelf.All[0].Path);
            Assert.True(FileShelf.All[0].Exists);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddFiles_PromotesDuplicatesToTop()
    {
        string f1 = Path.GetTempFileName();
        string f2 = Path.GetTempFileName();
        try
        {
            FileShelf.AddFiles([f1]);
            FileShelf.AddFiles([f2]);
            Assert.Equal(f2, FileShelf.All[0].Path);

            // Добавляем f1 повторно — он должен переместиться наверх
            FileShelf.AddFiles([f1]);
            Assert.Equal(2, FileShelf.Count);
            Assert.Equal(f1, FileShelf.All[0].Path);
            Assert.Equal(f2, FileShelf.All[1].Path);
        }
        finally
        {
            if (File.Exists(f1)) File.Delete(f1);
            if (File.Exists(f2)) File.Delete(f2);
        }
    }

    [Fact]
    public void Remove_RemovesTargetItem()
    {
        string f1 = Path.GetTempFileName();
        try
        {
            FileShelf.AddFiles([f1]);
            Assert.Equal(1, FileShelf.Count);

            Assert.True(FileShelf.Remove(f1));
            Assert.Equal(0, FileShelf.Count);
            Assert.False(FileShelf.Remove(f1));
        }
        finally
        {
            if (File.Exists(f1)) File.Delete(f1);
        }
    }

    [Fact]
    public void Clear_EmptiesAllItems()
    {
        string f1 = Path.GetTempFileName();
        try
        {
            FileShelf.AddFiles([f1]);
            Assert.Equal(1, FileShelf.Count);

            FileShelf.Clear();
            Assert.Equal(0, FileShelf.Count);
        }
        finally
        {
            if (File.Exists(f1)) File.Delete(f1);
        }
    }
}
