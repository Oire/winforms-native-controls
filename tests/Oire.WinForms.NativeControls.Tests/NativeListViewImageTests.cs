using System.Runtime.InteropServices;
using System.Windows.Forms;
using AwesomeAssertions;
using Xunit;

namespace Oire.WinForms.NativeControls.Tests;

public class NativeListViewImageTests {
    [Fact]
    public void Images_BeforeAndAfterCreation_ReachNativeRows() {
        StaRunner.Run(() => {
            using var images = CreateImages();
            using var list = new NativeListView { SmallImageList = images };
            var row = new NativeListViewItem("Network", "85%") { ImageIndex = 1 };
            list.Columns.Add(new NativeListViewColumn("Name", 160));
            list.Items.Add(row);
            list.Items.Add(new NativeListViewItem("No icon"));
            images.HandleCreated.Should().BeFalse();
            _ = list.Handle;

            AttachedImages(list).Should().Be(images.Handle);
            ReadImage(list, 0).Should().Be(1);
            ReadImage(list, 1).Should().Be(-2); // I_IMAGENONE, not I_IMAGECALLBACK.
            row.ImageIndex = 0;
            ReadImage(list, 0).Should().Be(0);
            row.ImageIndex = -1;
            ReadImage(list, 0).Should().Be(-2);
            list.Items.Add(new NativeListViewItem("Added later") { ImageIndex = 1 });
            ReadImage(list, 2).Should().Be(1);
        });
    }

    [Fact]
    public void Images_AcrossHandleRecreation_PreserveAssociationAndRows() {
        StaRunner.Run(() => {
            using var images = CreateImages();
            using var list = new RecreatableListView { SmallImageList = images };
            list.Columns.Add(new NativeListViewColumn("Name", 160));
            var row = new NativeListViewItem("Network") { ImageIndex = 1, Selected = true, Focused = true };
            list.Items.Add(row);
            _ = list.Handle;
            var imageHandle = images.Handle;

            list.RecreateForTest();
            AttachedImages(list).Should().Be(imageHandle);
            ImageList_GetImageCount(imageHandle).Should().Be(2);
            ReadImage(list, 0).Should().Be(1);
            row.Selected.Should().BeTrue();
            row.Focused.Should().BeTrue();

            // ColorDepth recreates the image-list handle and raises RecreateHandle.
            images.ColorDepth = ColorDepth.Depth32Bit;
            AttachedImages(list).Should().Be(images.Handle);
            ReadImage(list, 0).Should().Be(1);
        });
    }

    [Fact]
    public void Images_AssignReplaceRemoveAndDispose_UpdateNativeAssociation() {
        StaRunner.Run(() => {
            using var first = CreateImages();
            using var second = CreateImages();
            using var list = new NativeListView();
            _ = list.Handle;
            AttachedImages(list).Should().Be(IntPtr.Zero);
            list.SmallImageList = first;
            AttachedImages(list).Should().Be(first.Handle);
            list.SmallImageList = second;
            first.Dispose(); // The old image list must no longer affect this control.
            AttachedImages(list).Should().Be(second.Handle);
            list.SmallImageList = null;
            AttachedImages(list).Should().Be(IntPtr.Zero);
            ImageList_GetImageCount(second.Handle).Should().Be(2);
            list.SmallImageList = second;
            second.Dispose();
            list.SmallImageList.Should().BeNull();
            AttachedImages(list).Should().Be(IntPtr.Zero);
        });
    }

    [Fact]
    public void Images_SharedBetweenControls_OutliveEitherControl() {
        StaRunner.Run(() => {
            using var images = CreateImages();
            using var first = new NativeListView { SmallImageList = images };
            using var second = new NativeListView { SmallImageList = images };
            _ = first.Handle;
            _ = second.Handle;
            var imageHandle = images.Handle;
            first.Dispose();
            AttachedImages(second).Should().Be(imageHandle);
            ImageList_GetImageCount(imageHandle).Should().Be(2);
            images.ColorDepth = ColorDepth.Depth32Bit;
            first.SmallImageList.Should().BeNull();
            AttachedImages(second).Should().Be(images.Handle);
            images.Dispose();
            second.SmallImageList.Should().BeNull();
            AttachedImages(second).Should().Be(IntPtr.Zero);
        });
    }

    [Fact]
    public void ImageIndex_DetachedRow_RetainsIndexAndRejectsInvalidNegativeValues() {
        var row = new NativeListViewItem("Network");
        row.ImageIndex.Should().Be(-1);
        row.ImageIndex = 4;
        var act = () => row.ImageIndex = -2;
        act.Should().Throw<ArgumentOutOfRangeException>();
        row.ImageIndex.Should().Be(4);
    }

    private static ImageList CreateImages() {
        var images = new ImageList { ColorDepth = ColorDepth.Depth24Bit };
        images.Images.Add(SystemIcons.Information);
        images.Images.Add(SystemIcons.Warning);
        return images;
    }

    private static IntPtr AttachedImages(NativeListView list) => ListViewInterop.SendMessageW(
        list.ListHandle, ListViewInterop.LVM_GETIMAGELIST, ListViewInterop.LVSIL_SMALL, IntPtr.Zero);

    private static int ReadImage(NativeListView list, int index) {
        var item = new ListViewInterop.LVITEMW { Mask = ListViewInterop.LVIF_IMAGE, Item = index };
        ListViewInterop.SendMessageW(list.ListHandle, ListViewInterop.LVM_GETITEMW, IntPtr.Zero, ref item)
            .Should().NotBe(IntPtr.Zero);
        return item.Image;
    }

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern int ImageList_GetImageCount(IntPtr imageList);

    private sealed class RecreatableListView: NativeListView {
        internal void RecreateForTest() => RecreateHandle();
    }
}
