Feature: Product discovery
    As a shop user
    I want to find products by name or by stock level
    So that I can quickly see what can be sold and what needs restocking

Scenario: Searching by part of the name
    Given a product "Discovery ClearView Lens" with 5 units in stock
    And a product "Discovery Titanium Frame" with 50 units in stock
    When I search for "discovery clear"
    Then I see only "Discovery ClearView Lens"

Scenario: Finding products that are running low
    Given a product "Discovery Low Stock Item" with 2 units in stock
    When I list products with stock between 1 and 3
    Then every listed product has between 1 and 3 units in stock
    And the list includes "Discovery Low Stock Item"
