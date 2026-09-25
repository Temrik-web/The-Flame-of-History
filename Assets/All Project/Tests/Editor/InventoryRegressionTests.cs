using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class InventoryRegressionTests
{
    private GameObject root;
    private InventorySystem inventory;
    private ItemData item;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        root = new GameObject("Inventory test group");
        inventory = root.AddComponent<InventorySystem>();
        inventory.autoSaveOnQuit = false;
        inventory.showFloatingText = false;
        item = ScriptableObject.CreateInstance<ItemData>();
        item.itemId = "inventory_test_item";
        item.stackable = true;
        item.maxStack = 10;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Object.Destroy(root);
        Object.Destroy(item);
        yield return null;
        yield return new ExitPlayMode();
    }

    [Test]
    public void RemovingMoreThanAvailableDoesNotChangeInventory()
    {
        inventory.AddItem(item, 2);
        Assert.That(inventory.RemoveItem(item, 3), Is.False);
        Assert.That(inventory.CountItem(item), Is.EqualTo(2));
        Assert.That(inventory.RemoveItem(item, 2), Is.True);
        Assert.That(inventory.CountItem(item), Is.Zero);
    }

    [Test]
    public void FailedDropKeepsTheItem()
    {
        inventory.genericPickupPrefab = null;
        item.worldPrefab = null;
        inventory.AddItem(item, 1);

        inventory.DropOne(0);

        Assert.That(inventory.CountItem(item), Is.EqualTo(1));
    }

    [Test]
    public void DroppedPickupUsesTheAssignedItemAppearance()
    {
        item.rarity = ItemRarity.Rare;
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.name = "PickupTemplate";
        template.transform.SetParent(root.transform);
        template.AddComponent<Pickup>();
        item.worldPrefab = template;
        inventory.AddItem(item, 1);

        inventory.DropOne(0);

        GameObject clone = GameObject.Find("PickupTemplate(Clone)");
        Assert.That(clone, Is.Not.Null);
        clone.transform.SetParent(root.transform);
        Pickup dropped = clone.GetComponent<Pickup>();
        Assert.That(dropped.item, Is.SameAs(item));
        Assert.That(dropped.highlightColor, Is.EqualTo(item.RarityColor));
        Assert.That(dropped.GetComponentInChildren<Light>().color, Is.EqualTo(item.RarityColor));
        Assert.That(inventory.CountItem(item), Is.Zero);
    }
}
