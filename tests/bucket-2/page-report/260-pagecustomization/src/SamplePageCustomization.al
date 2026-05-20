pagecustomization "PC Sample Customization" customizes "Sample Page To Customize"
{
    layout
    {
        modify("No.")
        {
            Visible = false;
        }
    }
}

page 50067 "Sample Page To Customize"
{
    PageType = Card;

    layout
    {
        area(Content)
        {
            field("No."; 'test')
            {
            }
        }
    }
}
