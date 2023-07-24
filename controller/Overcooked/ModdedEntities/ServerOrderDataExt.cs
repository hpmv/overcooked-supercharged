using OrderController;

public static class ServerOrderDataExt {
    public static ServerOrderData WithRemaining(this ServerOrderData data, float remaining) {
        return new ServerOrderData(data.ID, data.RecipeListEntry, data.Lifetime) {
            Remaining = remaining,
        };
    }
}